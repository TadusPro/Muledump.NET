using System.ComponentModel.DataAnnotations;

namespace MDTadusMod.Data
{
    public class Account
    {
        public Guid Id { get; set; }

        [Required]
        public string Email { get; set; }

        [Required]
        public string Password { get; set; }
    }
}