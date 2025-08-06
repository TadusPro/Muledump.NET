using System.Collections.Generic;
using System.Xml.Serialization;

namespace MDTadusMod.Data
{
    public class Pet
    {
        [XmlAttribute]
        public int InstanceId { get; set; }
        [XmlAttribute]
        public string Name { get; set; }
        [XmlAttribute]
        public int ObjectType { get; set; }
        [XmlAttribute]
        public int Rarity { get; set; }
        [XmlAttribute]
        public int MaxAbilityPower { get; set; }
        [XmlAttribute]
        public int Skin { get; set; }
        [XmlAttribute]
        public int Shader { get; set; }
        [XmlAttribute]
        public string CreatedOn { get; set; }

        [XmlArray("Abilities")]
        [XmlArrayItem("Ability")]
        public List<PetAbility> Abilities { get; set; } = new List<PetAbility>();
    }

    public class PetAbility
    {
        [XmlAttribute]
        public int Type { get; set; }
        [XmlAttribute]
        public int Power { get; set; }
        [XmlAttribute]
        public int Points { get; set; }
    }

    public class PetInventoryData
    {
        public List<Item> Items { get; set; } = new List<Item>();
    }
}