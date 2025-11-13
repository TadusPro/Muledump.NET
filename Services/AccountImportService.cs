using MDTadusMod.Data;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Devices;
using System.Text.Json;
using System.Linq;

namespace MDTadusMod.Services
{
    public sealed class AccountImportService
    {
        private readonly AccountService _accountService;

        public AccountImportService(AccountService accountService)
        {
            _accountService = accountService;
        }

        public record ImportResult(int Added, int Duplicates, int Invalid, int Total);

        public async Task<ImportResult> ImportViaFilePickerAsync(List<Account> accounts, CancellationToken ct = default)
        {
            var pickOptions = new PickOptions
            {
                PickerTitle = "Select accounts file",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI,       new[] { ".xml", ".txt", ".csv", ".log", ".js", ".json", ".bin" } },
                    { DevicePlatform.Android,     new[] { "text/xml", "application/xml", "text/plain", "text/csv", "application/javascript", "text/javascript", "application/json", "text/json", "application/octet-stream" } },
                    { DevicePlatform.iOS,         new[] { "public.xml", "public.plain-text", "public.comma-separated-values-text", "com.netscape.javascript-source", "public.json", "public.data" } },
                    { DevicePlatform.MacCatalyst, new[] { "public.xml", "public.plain-text", "public.comma-separated-values-text", "com.netscape.javascript-source", "public.json", "public.data" } },
                })
            };

            var file = await FilePicker.Default.PickAsync(pickOptions);
            if (file is null) return new ImportResult(0, 0, 0, 0);

            await using var stream = await file.OpenReadAsync();
            var parsed = await ParseFromStreamAsync(stream, file.FileName, ct);

            if (parsed.Count == 0) return new ImportResult(0, 0, 0, 0);

            var existingByEmail = new HashSet<string>(accounts.Select(a => a.Email), StringComparer.OrdinalIgnoreCase);

            int added = 0, duplicates = 0, invalid = 0;
            foreach (var acc in parsed)
            {
                if (string.IsNullOrWhiteSpace(acc.Email) || string.IsNullOrWhiteSpace(acc.Password))
                {
                    invalid++;
                    continue;
                }

                if (!existingByEmail.Contains(acc.Email))
                {
                    if (acc.Id == Guid.Empty) acc.Id = Guid.NewGuid();
                    accounts.Add(acc);
                    existingByEmail.Add(acc.Email);
                    added++;
                }
                else
                {
                    duplicates++;
                }
            }

            await _accountService.SaveAccountsAsync(accounts);
            return new ImportResult(added, duplicates, invalid, parsed.Count);
        }

        // Now buffers the stream to bytes so we can try multiple strategies safely, including binary heuristics.
        public async Task<List<Account>> ParseFromStreamAsync(Stream stream, string? fileNameHint, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();

            // 1) Try strict XML when extension suggests XML
            try
            {
                var ext = Path.GetExtension(fileNameHint ?? "").ToLowerInvariant();
                if (ext == ".xml")
                {
                    var ser = new XmlSerializer(typeof(List<Account>));
                    using var xms = new MemoryStream(bytes);
                    if (ser.Deserialize(xms) is List<Account> xmlAccounts && xmlAccounts.Count > 0)
                        return xmlAccounts;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AccountImportService] XML deserialize failed: {ex}");
            }

            // Prepare text view once
            var content = SafeDecodeUtf8(bytes);

            // 2) DB.json (by name and shape)
            if (LooksLikeDbJson(fileNameHint, content))
            {
                var db = ParseFromDbJson(content);
                if (db.Count > 0) return db;
            }

            // 3) accounts.bin (by exact filename)
            if (!string.IsNullOrEmpty(fileNameHint) &&
                string.Equals(Path.GetFileName(fileNameHint), "accounts.bin", StringComparison.OrdinalIgnoreCase))
            {
                var bin = ParseFromAccountsBinJson(content);
                if (bin.Count > 0) return bin;
            }

            // 4) Exalt binary (serialized C# class) detection by markers, then binary heuristics
            if (LooksLikeExaltBinary(bytes, content))
            {
                var exalt = ParseFromBinaryHeuristics(bytes);
                if (exalt.Count > 0) return exalt;
            }

            // 5) accounts.js
            var jsParsed = ParseFromAccountsJs(content);
            if (jsParsed.Count > 0)
                return jsParsed;

            // 6) Generic text
            var textParsed = ParseFromString(content);
            if (textParsed.Count > 0)
                return textParsed;

            // 7) Final fallback: generic binary heuristics
            return ParseFromBinaryHeuristics(bytes);
        }

        private static bool LooksLikeDbJson(string? fileNameHint, string content)
        {
            if (!string.IsNullOrEmpty(fileNameHint) && string.Equals(Path.GetFileName(fileNameHint), "DB.json", StringComparison.OrdinalIgnoreCase))
                return true;

            if (content.IndexOf("\"accounts\"", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (content.IndexOf("\"username\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 content.IndexOf("\"password\"", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }
            return false;
        }

        private static bool LooksLikeExaltBinary(byte[] bytes, string content)
        {
            if (content.IndexOf("<Email>k__BackingField", StringComparison.OrdinalIgnoreCase) >= 0 &&
                content.IndexOf("<Password>k__BackingField", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (content.IndexOf("SavedAccounts", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        public List<Account> ParseFromAccountsBinJson(string content)
        {
            var results = new List<Account>();
            if (string.IsNullOrWhiteSpace(content)) return results;

            try
            {
                var opts = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
                using var doc = JsonDocument.Parse(content, opts);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return results;

                if (!doc.RootElement.TryGetProperty("accounts", out var accountsEl) || accountsEl.ValueKind != JsonValueKind.Array)
                    return results;

                foreach (var accEl in accountsEl.EnumerateArray())
                {
                    if (accEl.ValueKind != JsonValueKind.Object) continue;
                    var guid = accEl.TryGetProperty("guid", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : null;
                    var pwd  = accEl.TryGetProperty("password", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

                    if (string.IsNullOrWhiteSpace(guid) || string.IsNullOrWhiteSpace(pwd)) continue;
                    if (!IsEmail(guid)) continue;

                    results.Add(new Account { Id = Guid.NewGuid(), Email = guid.Trim(), Password = pwd.Trim() });
                }

                results = results
                    .GroupBy(a => a.Email, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AccountImportService] ParseFromAccountsBinJson failed: {ex}");
            }

            return results;
        }

        public List<Account> ParseFromDbJson(string content)
        {
            var results = new List<Account>();
            if (string.IsNullOrWhiteSpace(content)) return results;

            try
            {
                var opts = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
                using var doc = JsonDocument.Parse(content, opts);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return results;

                if (!doc.RootElement.TryGetProperty("accounts", out var accountsEl) || accountsEl.ValueKind != JsonValueKind.Array)
                    return results;

                foreach (var accEl in accountsEl.EnumerateArray())
                {
                    if (accEl.ValueKind != JsonValueKind.Object) continue;
                    var email = accEl.TryGetProperty("username", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                    var pwd   = accEl.TryGetProperty("password", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

                    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pwd)) continue;
                    if (!IsEmail(email)) continue;

                    results.Add(new Account { Id = Guid.NewGuid(), Email = email.Trim(), Password = pwd.Trim() });
                }

                results = results
                    .GroupBy(a => a.Email, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AccountImportService] ParseFromDbJson failed: {ex}");
            }

            return results;
        }

        public List<Account> ParseFromString(string content)
        {
            var result = new List<Account>();
            if (string.IsNullOrWhiteSpace(content)) return result;

            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;

                line = Unquote(line);
                var split = SplitOnFirst(line, new[] { ':', ',', ';', '|', '\t', ' ' });
                if (split is not null && IsEmail(split.Value.left))
                {
                    var email = split.Value.left.Trim();
                    var password = split.Value.right.Trim();
                    password = TrimInlineComment(password);
                    password = Unquote(password);
                    if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
                        result.Add(new Account { Id = Guid.NewGuid(), Email = email, Password = password });
                }
            }

            if (result.Count < 1)
            {
                var rx = new Regex(@"(?im)^(?<email>[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,})\s*[:|,;\t ]\s*(?<pwd>.+?)\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline);

                foreach (Match m in rx.Matches(content))
                {
                    var email = m.Groups["email"].Value.Trim();
                    var pwd = m.Groups["pwd"].Value.Trim();
                    pwd = TrimInlineComment(pwd);
                    pwd = Unquote(pwd);

                    if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(pwd))
                        result.Add(new Account { Id = Guid.NewGuid(), Email = email, Password = pwd });
                }
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduped = new List<Account>(result.Count);
            foreach (var a in result)
                if (seen.Add(a.Email)) deduped.Add(a);

            return deduped;
        }

        public List<Account> ParseFromAccountsJs(string content)
        {
            var results = new List<Account>();
            if (string.IsNullOrWhiteSpace(content)) return results;

            var accountsObj = ExtractJsObjectAfterAssignment(content, "accounts");
            if (accountsObj is null)
                return results;

            var pairRx = new Regex(@"(?s)(['""])(?<key>.*?)\1\s*:\s*(['""])(?<val>.*?)\3", RegexOptions.Compiled);
            foreach (Match m in pairRx.Matches(accountsObj))
            {
                var email = UnescapeJsString(m.Groups["key"].Value.Trim());
                var pwd = UnescapeJsString(m.Groups["val"].Value.Trim());

                if (IsEmail(email) && !string.IsNullOrWhiteSpace(pwd))
                    results.Add(new Account { Id = Guid.NewGuid(), Email = email, Password = pwd });
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return results.Where(a => seen.Add(a.Email)).ToList();
        }

        private static string? ExtractJsObjectAfterAssignment(string content, string variableName)
        {
            var rx = new Regex($@"(?im)\b{Regex.Escape(variableName)}\s*=\s*\{{", RegexOptions.Compiled);
            var m = rx.Match(content);
            if (!m.Success) return null;

            var openIdx = content.IndexOf('{', m.Index);
            if (openIdx < 0) return null;

            int depth = 0;
            bool inString = false;
            char quote = '\0';
            bool escape = false;

            for (int i = openIdx; i < content.Length; i++)
            {
                char c = content[i];

                if (inString)
                {
                    if (escape) { escape = false; continue; }
                    if (c == '\\') { escape = true; continue; }
                    if (c == quote) { inString = false; continue; }
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inString = true;
                    quote = c;
                    continue;
                }

                if (c == '{') { depth++; continue; }
                if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return content.Substring(openIdx + 1, i - openIdx - 1);
                }
            }
            return null;
        }

        // Best-effort UTF-8 decode
        private static string SafeDecodeUtf8(byte[] bytes) => Encoding.UTF8.GetString(bytes);

        // Heuristic binary parser: extract printable strings, then map email -> next plausible string as password.
        private static List<Account> ParseFromBinaryHeuristics(byte[] bytes)
        {
            var results = new List<Account>();
            var tokens = ExtractPrintableStrings(bytes, minLen: 2);
            if (tokens.Count == 0) return results;

            // Skip obvious labels and framework/type markers
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "email","emails","username","user","guid","name","alias","account","accounts","password","pass",
                "System.Guid","ExaltKitGUI.Account","ExaltKitGUI.Settings","SavedAccounts",
                "<Label>k__BackingField","<Icon>k__BackingField","<Email>k__BackingField","<Password>k__BackingField"
            };

            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i].Trim();
                if (!IsEmail(t)) continue;

                // Find the next plausible password candidate
                string? pwd = null;
                for (int j = i + 1; j < tokens.Count; j++)
                {
                    var cand = tokens[j].Trim();
                    if (cand.Length == 0) continue;
                    if (skip.Contains(cand)) continue;
                    if (IsEmail(cand)) continue;
                    if (cand.Length > 256) continue; // implausibly long
                    pwd = cand;
                    break;
                }

                if (!string.IsNullOrWhiteSpace(pwd))
                    results.Add(new Account { Id = Guid.NewGuid(), Email = t, Password = pwd! });
            }

            // Dedup by email
            return results
                .GroupBy(a => a.Email, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        // Extract sequences of printable ASCII substrings from binary data
        private static List<string> ExtractPrintableStrings(byte[] data, int minLen = 3)
        {
            var list = new List<string>();
            var sb = new StringBuilder();

            for (int i = 0; i < data.Length; i++)
            {
                var b = data[i];
                if (b == 0x09 || b == 0x20 || (b >= 0x21 && b <= 0x7E))
                {
                    sb.Append((char)b);
                }
                else
                {
                    if (sb.Length >= minLen)
                        list.Add(sb.ToString());
                    sb.Clear();
                }
            }
            if (sb.Length >= minLen) list.Add(sb.ToString());

            // Split very long chunks on whitespace to increase accuracy
            var split = new List<string>(list.Count);
            foreach (var chunk in list)
            {
                if (chunk.Length > 1024)
                    split.AddRange(chunk.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                else
                    split.Add(chunk);
            }
            return split;
        }

        private static string Unquote(string s)
        {
            if (s.Length >= 2)
            {
                if ((s.StartsWith("\"") && s.EndsWith("\"")) || (s.StartsWith("'") && s.EndsWith("'")))
                    return s.Substring(1, s.Length - 2);
            }
            return s;
        }

        private static string UnescapeJsString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\') { sb.Append(c); continue; }
                if (i + 1 >= s.Length) { sb.Append('\\'); break; }

                char n = s[++i];
                switch (n)
                {
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case '\'': sb.Append('\''); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 < s.Length && int.TryParse(s.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out var code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        else
                        {
                            sb.Append("\\u");
                        }
                        break;
                    default:
                        sb.Append(n); break;
                }
            }
            return sb.ToString();
        }

        private static bool IsEmail(string s)
        {
            return Regex.IsMatch(s, @"^[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}$", RegexOptions.IgnoreCase);
        }

        private static (string left, string right)? SplitOnFirst(string input, char[] seps)
        {
            var idx = input.IndexOfAny(seps);
            if (idx <= 0 || idx >= input.Length - 1) return null;
            var left = input[..idx];
            var right = input[(idx + 1)..];
            return (left, right);
        }

        private static string TrimInlineComment(string s)
        {
            var tokens = new[] { " //", "\t//", " #", "\t#", " ;", "\t;" };
            var cut = s;
            foreach (var t in tokens)
            {
                var i = cut.IndexOf(t, StringComparison.Ordinal);
                if (i >= 0) { cut = cut[..i]; break; }
            }
            return cut.Trim();
        }
    }
}