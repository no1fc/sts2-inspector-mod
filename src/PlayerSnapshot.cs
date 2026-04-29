using System.Collections.Generic;
using System.Linq;

namespace TestMode1
{
    public record HandCardEntry(string Name, int? Cost, int? EffectiveDamage, int? EffectiveBlock);
    public record BuffEntry(string Name, int Amount, bool IsDebuff);

    public class PlayerSnapshot
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public string CharacterName { get; set; } = "";
        public List<string> DeckCardIds { get; set; } = new();
        public List<string> HandCardIds { get; set; } = new();
        public List<string> RelicIds    { get; set; } = new();
        public List<string> PotionIds   { get; set; } = new();

        public int DeckCount   => DeckCardIds.Count;
        public int HandCount   => HandCardIds.Count;
        public int RelicCount  => RelicIds.Count;
        public int PotionCount => PotionIds.Count;

        public Dictionary<string, string> NameToDescription { get; set; } = new();
        public Dictionary<string, string> NameToImageKey    { get; set; } = new();
        // 표시명 -> "피해: N | 방어: N | 코스트: N" 형식의 스탯 문자열
        public Dictionary<string, string> NameToStats       { get; set; } = new();

        public List<HandCardEntry> HandCardEntries { get; set; } = new();

        public List<BuffEntry> Buffs { get; set; } = new();
        public int BuffCount => Buffs.Count;

        public string Hash { get; private set; } = "";

        public void ComputeHash()
        {
            Hash = string.Join(",", DeckCardIds) + "|" +
                   string.Join(",", HandCardIds) + "|" +
                   string.Join(",", RelicIds)    + "|" +
                   string.Join(",", PotionIds)   + "|" +
                   string.Join(",", Buffs.Select(b => $"{b.Name}:{b.Amount}"));
        }

        public bool IsDirtyComparedTo(PlayerSnapshot other) =>
            other == null || Hash != other.Hash;
    }
}
