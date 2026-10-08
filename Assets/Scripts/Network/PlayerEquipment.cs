using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Brickcraft.Network
{
    /// <summary>An equipped armor piece, stored as JSON in the players table.</summary>
    public class ArmorItem
    {
        /// <summary>The namespaced item ID of the armor piece.</summary>
        [JsonProperty("id")]
        public string id;

        /// <summary>The quantity of the item in the slot (typically 1).</summary>
        [JsonProperty("count")]
        public int count = 1;

        /// <summary>Data components tracking metadata, enchantments, and item modifications.</summary>
        [JsonProperty("components", NullValueHandling = NullValueHandling.Ignore)]
        public List<JToken> components;
    }

    /// <summary>What a player wears, every slot is optional.</summary>
    public class PlayerEquipment
    {
        [JsonProperty("head", NullValueHandling = NullValueHandling.Ignore)]
        public ArmorItem head;

        [JsonProperty("chest", NullValueHandling = NullValueHandling.Ignore)]
        public ArmorItem chest;

        [JsonProperty("legs", NullValueHandling = NullValueHandling.Ignore)]
        public ArmorItem legs;

        [JsonProperty("feet", NullValueHandling = NullValueHandling.Ignore)]
        public ArmorItem feet;

        [JsonIgnore]
        public bool IsEmpty {
            get { return head == null && chest == null && legs == null && feet == null; }
        }

        /// <summary>Null for no equipment, so the column stays empty.</summary>
        public static string ToJson(PlayerEquipment equipment) {
            return equipment == null || equipment.IsEmpty ? null : JsonConvert.SerializeObject(equipment);
        }

        /// <summary>Null when the column is empty.</summary>
        public static PlayerEquipment FromJson(string json) {
            return string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<PlayerEquipment>(json);
        }
    }
}
