using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Runs;

namespace TestMode1
{
    public partial class PlayerDataPoller : Node
    {
        public event Action<PlayerSnapshot> PlayerUpdated;
        public event Action<string> PlayerRemoved;

        private readonly Dictionary<string, PlayerSnapshot> _lastSnapshots = new();
        private string _localPlayerId;
        private bool _loggedCollType;

        public PlayerSnapshot GetLastSnapshot(string playerId) =>
            _lastSnapshots.TryGetValue(playerId, out var s) ? s : null;

        public override void _Ready()
        {
            var timer = new Timer
            {
                WaitTime = ModSettings.Instance.PollIntervalSeconds,
                Autostart = true
            };
            timer.Timeout += OnPollTick;
            AddChild(timer);
        }

        private void OnPollTick()
        {
            try
            {
                if (!IsRunActive()) return;

                var currentIds = GetCurrentPlayerIds();
                GD.Print($"[Inspector] Poll: {currentIds.Count} remote player(s) found");

                foreach (var id in _lastSnapshots.Keys.Except(currentIds).ToList())
                {
                    _lastSnapshots.Remove(id);
                    PlayerRemoved?.Invoke(id);
                }

                foreach (var id in currentIds)
                {
                    var snap = TakeSnapshot(id);
                    if (snap == null) continue;
                    snap.ComputeHash();

                    if (!_lastSnapshots.TryGetValue(id, out var last) || snap.IsDirtyComparedTo(last))
                    {
                        _lastSnapshots[id] = snap;
                        PlayerUpdated?.Invoke(snap);
                    }
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"[Inspector] Poll error: {e.Message}\n{e.StackTrace}");
            }
        }

        // ── 게임 API 접근 ────────────────────────────────────────────────

        private static readonly System.Reflection.BindingFlags _allInstance =
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance;

        private static object GetProp(object obj, string name) =>
            obj?.GetType().GetProperty(name, _allInstance)?.GetValue(obj);

        private static readonly System.Reflection.PropertyInfo _stateProp =
            typeof(RunManager).GetProperty("State",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

        private static IPlayerCollection GetPlayerCollection()
        {
            var rm = RunManager.Instance;
            if (rm == null) return null;
            return _stateProp?.GetValue(rm) as IPlayerCollection;
        }

        private bool IsRunActive()
        {
            try
            {
                var rm = RunManager.Instance;
                if (rm == null) return false;

                var coll = GetPlayerCollection();
                if (coll != null)
                {
                    if (!_loggedCollType)
                    {
                        GD.Print($"[Inspector] Active — state type: {coll.GetType().FullName}");
                        _loggedCollType = true;
                    }
                    return true;
                }

                if (GetSinglePlayerFallback() != null)
                {
                    if (!_loggedCollType)
                    {
                        var state = _stateProp?.GetValue(rm);
                        GD.Print($"[Inspector] Active (solo fallback) — state type: {state?.GetType().FullName}");
                        _loggedCollType = true;
                    }
                    return true;
                }

                return false;
            }
            catch { return false; }
        }

        private List<string> GetCurrentPlayerIds()
        {
            try
            {
                var coll = GetPlayerCollection();
                if (coll != null)
                {
                    var ids = EnumeratePlayers(coll)
                        .Select(GetPlayerId)
                        .Where(id => id != null)
                        .ToList();
                    GD.Print($"[Inspector] Player IDs: [{string.Join(", ", ids)}]");
                    return ids;
                }

                var player = GetSinglePlayerFallback();
                if (player != null)
                {
                    _localPlayerId ??= ResolveLocalPlayerId();
                    var id = GetPlayerId(player) ?? _localPlayerId ?? "local";
                    GD.Print($"[Inspector] Player IDs (solo fallback): [{id}]");
                    return new List<string> { id };
                }

                return new();
            }
            catch (Exception e)
            {
                GD.PrintErr($"[Inspector] GetCurrentPlayerIds: {e.Message}");
                return new();
            }
        }

        private PlayerSnapshot TakeSnapshot(string playerId)
        {
            try
            {
                var coll = GetPlayerCollection();
                if (coll != null)
                {
                    foreach (var player in EnumeratePlayers(coll))
                    {
                        if (GetPlayerId(player) == playerId)
                            return BuildSnapshot(playerId, player);
                    }
                    return null;
                }

                var solo = GetSinglePlayerFallback();
                if (solo != null)
                    return BuildSnapshot(playerId, solo);

                return null;
            }
            catch (Exception e)
            {
                GD.PrintErr($"[Inspector] TakeSnapshot({playerId}): {e.Message}");
                return null;
            }
        }

        private static object GetSinglePlayerFallback()
        {
            try
            {
                var state = _stateProp?.GetValue(RunManager.Instance);
                if (state == null) return null;
                return GetProp(state, "Player")
                    ?? GetProp(state, "CurrentPlayer")
                    ?? GetProp(state, "LocalPlayer");
            }
            catch { return null; }
        }

        // ── Reflection 헬퍼 ─────────────────────────────────────────────

        private static IEnumerable<object> EnumeratePlayers(IPlayerCollection coll)
        {
            var prop = coll.GetType().GetProperty("Players", _allInstance)
                    ?? typeof(IPlayerCollection).GetProperty("Players", _allInstance);

            if (prop == null)
            {
                GD.PrintErr($"[Inspector] Players 프로퍼티를 찾을 수 없음 (type={coll.GetType().FullName})");
                yield break;
            }

            if (prop.GetValue(coll) is System.Collections.IEnumerable list)
                foreach (var p in list) yield return p;
        }

        private static string GetPlayerId(object player) =>
            GetProp(player, "NetId")?.ToString();

        private static string ResolveLocalPlayerId()
        {
            try
            {
                var lobby = RunManager.Instance.RunLobby;
                if (lobby == null) return null;
                var localPlayer = GetProp(lobby, "LocalPlayer");
                return GetProp(localPlayer, "NetId")?.ToString();
            }
            catch { return null; }
        }

        private static PlayerSnapshot BuildSnapshot(string playerId, object player)
        {
            var snap = new PlayerSnapshot { PlayerId = playerId };

            var character = GetProp(player, "Character");
            if (character != null)
            {
                snap.PlayerName = ResolveLocString(GetProp(character, "Title"))
                               ?? ResolveLocString(GetProp(character, "Name"))
                               ?? playerId;
            }
            else
            {
                snap.PlayerName = playerId;
            }

            var deck    = GetProp(player, "Deck");
            var relics  = GetProp(player, "Relics");
            var potions = GetProp(player, "Potions");
            var hand    = FindHandPile(GetProp(player, "Piles"));

            snap.DeckCardIds = ExtractTitles(deck);
            snap.RelicIds    = ExtractTitles(relics);
            snap.PotionIds   = ExtractTitles(potions);
            snap.HandCardIds = ExtractTitles(hand);

            snap.NameToDescription = new Dictionary<string, string>();
            ExtractDescriptions(deck,    snap.NameToDescription);
            ExtractDescriptions(relics,  snap.NameToDescription);
            ExtractDescriptions(potions, snap.NameToDescription);
            ExtractDescriptions(hand,    snap.NameToDescription);

            snap.NameToImageKey = new Dictionary<string, string>();
            ExtractImageKeys(deck,    snap.NameToImageKey);
            ExtractImageKeys(relics,  snap.NameToImageKey);
            ExtractImageKeys(potions, snap.NameToImageKey);
            ExtractImageKeys(hand,    snap.NameToImageKey);

            snap.NameToStats = new Dictionary<string, string>();
            ExtractStats(deck,    snap.NameToStats);
            ExtractStats(relics,  snap.NameToStats);
            ExtractStats(potions, snap.NameToStats);
            ExtractStats(hand,    snap.NameToStats);

            GD.Print($"[Inspector] Snap [{playerId}] name={snap.PlayerName} deck={snap.DeckCount} relics={snap.RelicCount} potions={snap.PotionCount} hand={snap.HandCount}");
            return snap;
        }

        // ── 아이템 이름 통합 해석 ──────────────────────────────────────────
        // 카드: Title (String) = "타격" → 직접 반환
        // 유물/포션: Title (LocString) 해석 실패 → HoverTip.Title (struct, 한국어 평문) 사용

        private static string ResolveDisplayName(object item)
        {
            // 카드처럼 Title이 이미 plain String인 경우
            var titleVal = GetProp(item, "Title");
            if (titleVal is string s && !string.IsNullOrEmpty(s))
                return s;

            // 유물/포션: HoverTip struct의 Title (한국어 평문)
            var hoverTip = GetProp(item, "HoverTip");
            if (hoverTip != null)
            {
                var htTitle = GetProp(hoverTip, "Title")?.ToString();
                if (!string.IsNullOrEmpty(htTitle)) return htTitle;
            }

            // 최후 fallback
            return ResolveLocString(titleVal)
                ?? GetProp(item, "Name")?.ToString()
                ?? item.GetType().Name;
        }

        // ── Extract 메서드들 ──────────────────────────────────────────────

        private static List<string> ExtractTitles(object collection)
        {
            var list = new List<string>();
            if (collection == null) return list;

            foreach (var item in UnwrapCollection(collection))
            {
                if (item == null) continue;
                list.Add(ResolveDisplayName(item));
            }
            return list;
        }

        private static void ExtractImageKeys(object collection, Dictionary<string, string> dict)
        {
            if (collection == null) return;

            foreach (var item in UnwrapCollection(collection))
            {
                if (item == null) continue;

                var displayName = ResolveDisplayName(item);
                if (dict.ContainsKey(displayName)) continue;

                var rawKey = GetProp(item, "Key")?.ToString()
                          ?? GetProp(item, "CardModelId")?.ToString()
                          ?? GetProp(item, "Id")?.ToString();

                if (rawKey == null) continue;
                var snakeKey = ImageCache.ToSnakeCase(rawKey);
                if (!string.IsNullOrEmpty(snakeKey))
                {
                    dict[displayName] = snakeKey;
                    GD.Print($"[Inspector] ImageKey: {displayName} → {snakeKey}");
                }
            }
        }

        private static void ExtractDescriptions(object collection, Dictionary<string, string> dict)
        {
            if (collection == null) return;

            foreach (var item in UnwrapCollection(collection))
            {
                if (item == null) continue;
                var t = item.GetType();

                var name = ResolveDisplayName(item);
                if (dict.ContainsKey(name)) continue;

                // 1순위: HoverTip.Description — 유물/포션에서 이미 한국어 평문으로 확인됨
                var hoverTip = GetProp(item, "HoverTip");
                if (hoverTip != null)
                {
                    var htDesc = GetProp(hoverTip, "Description")?.ToString();
                    if (!string.IsNullOrEmpty(htDesc))
                    {
                        dict[name] = StripRichText(htDesc);
                        continue;
                    }
                }

                // 2순위: DynamicDescription / Description (LocString) 및 기존 fallback chain
                object descObj = GetProp(item, "DynamicDescription")
                              ?? GetProp(item, "Description")
                              ?? GetProp(item, "RawDescription")
                              ?? GetProp(item, "CardDescription");

                if (descObj == null)
                {
                    try { descObj = item.GetType().GetMethod("GetDescription", _allInstance)?.Invoke(item, null); }
                    catch { }
                }

                if (descObj == null)
                {
                    var sub = GetProp(item, "CardData") ?? GetProp(item, "Data") ?? GetProp(item, "Model");
                    if (sub != null)
                    {
                        descObj = GetProp(sub, "Description") ?? GetProp(sub, "RawDescription");
                    }
                }

                var desc = ResolveLocString(descObj) ?? "";
                if (!string.IsNullOrEmpty(desc))
                    dict[name] = StripRichText(desc);
            }
        }

        private static void ExtractStats(object collection, Dictionary<string, string> dict)
        {
            if (collection == null) return;

            foreach (var item in UnwrapCollection(collection))
            {
                if (item == null) continue;

                var name = ResolveDisplayName(item);
                if (dict.ContainsKey(name)) continue;

                var stats = BuildStatsString(item);
                if (!string.IsNullOrEmpty(stats))
                {
                    dict[name] = stats;
                    GD.Print($"[Inspector] Stats: {name} -> {stats}");
                }
            }
        }

        // ── 스탯 추출 ─────────────────────────────────────────────────────

        private static string BuildStatsString(object item)
        {
            if (item == null) return null;
            var t = item.GetType();

            int? damage = null, block = null, cost = null;

            // 코스트: CanonicalEnergyCost (덤프에서 직접 확인)
            cost ??= TryGetIntProp(item, "CanonicalEnergyCost", "BaseCost", "BaseEnergyCost", "EnergyCost", "Cost");

            // 피해/방어: CanonicalVars 열거 (internal 프로퍼티 — NonPublic 플래그 필수)
            var canonVarsSource = GetProp(item, "CanonicalVars");
            if (!(canonVarsSource is System.Collections.IEnumerable))
            {
                var ci = GetProp(item, "CanonicalInstance");
                if (ci != null)
                    canonVarsSource = GetProp(ci, "CanonicalVars");
            }

            if (canonVarsSource is System.Collections.IEnumerable canonVars)
            {
                foreach (var v in canonVars)
                {
                    if (v == null) continue;
                    var vt = v.GetType();

                    // CanonicalVars가 Dictionary<string, DynamicVar>일 때 KeyValuePair 처리
                    object valueObj = v;
                    var kvValue = vt.GetProperty("Value")?.GetValue(v);
                    if (kvValue != null && kvValue is not int && kvValue is not string)
                        valueObj = kvValue;

                    var key = GetProp(v, "Name")?.ToString()
                           ?? GetProp(v, "Key")?.ToString()
                           ?? GetProp(v, "Id")?.ToString();
                    var val = TryGetIntProp(valueObj, "IntValue", "BaseValue", "CanonicalValue", "Value", "CurrentValue");
                    if (key == null || val == null) continue;
                    switch (key.ToUpperInvariant())
                    {
                        case "D": case "DAMAGE": case "DMG": case "ATK": case "ATTACK":
                            damage = val; break;
                        case "B": case "BLOCK": case "DEF": case "DEFENSE": case "SHIELD":
                            block = val; break;
                    }
                }
            }

            // fallback: 직접 프로퍼티 탐색
            damage ??= TryGetIntProp(item, "BaseDamage", "DamageAmount", "Damage");
            block  ??= TryGetIntProp(item, "BaseBlock",  "BlockAmount",  "Block");
            damage ??= TryInvokeIntMethod(item, "GetBaseDamage", "GetDamage");
            block  ??= TryInvokeIntMethod(item, "GetBaseBlock",  "GetBlock");

            // 서브 오브젝트 fallback (CardData / Data / Model)
            var sub = GetProp(item, "CardData") ?? GetProp(item, "Data") ?? GetProp(item, "Model");
            if (sub != null)
            {
                damage ??= TryGetIntProp(sub, "BaseDamage", "DamageAmount", "Damage");
                block  ??= TryGetIntProp(sub, "BaseBlock",  "BlockAmount",  "Block");
                cost   ??= TryGetIntProp(sub, "CanonicalEnergyCost", "BaseCost", "EnergyCost", "Cost");
            }

            // 효과 컬렉션 fallback (Effects / CardEffects / Actions)
            var effects = GetProp(item, "Effects") ?? GetProp(item, "CardEffects") ?? GetProp(item, "Actions");
            if (effects is System.Collections.IEnumerable effectList)
            {
                foreach (var eff in effectList)
                {
                    if (eff == null) continue;
                    damage ??= TryGetIntProp(eff, "BaseDamage", "DamageAmount", "Damage", "Amount");
                    block  ??= TryGetIntProp(eff, "BaseBlock",  "BlockAmount",  "Block",  "Amount");
                }
            }

            var parts = new System.Text.StringBuilder();
            if (cost.HasValue)
            {
                var costStr = cost.Value == -1 ? "X" : cost.Value.ToString();
                parts.Append($"코스트: {costStr}  ");
            }
            if (damage.HasValue && damage.Value > 0) parts.Append($"피해: {damage.Value}  ");
            if (block.HasValue  && block.Value  > 0) parts.Append($"방어: {block.Value}");
            return parts.Length > 0 ? parts.ToString().Trim() : null;
        }

        // ── 공통 유틸리티 ─────────────────────────────────────────────────

        // Piles[] 배열에서 Type="Hand" CardPile 반환 (전투 중에만 존재)
        private static object FindHandPile(object pilesRaw)
        {
            if (pilesRaw is not System.Collections.IEnumerable pilesEnum) return null;
            foreach (var pile in pilesEnum)
            {
                if (pile == null) continue;
                var pileType = GetProp(pile, "Type")?.ToString();
                if (pileType == "Hand") return pile;
            }
            return null;
        }

        // wrapper 타입(DeckModel 등)을 unwrap해 IEnumerable 반환
        private static System.Collections.IEnumerable UnwrapCollection(object collection)
        {
            if (collection is System.Collections.IEnumerable direct)
                return direct;

            var inner = GetProp(collection, "Cards")
                     ?? GetProp(collection, "Items")
                     ?? GetProp(collection, "All")
                     ?? GetProp(collection, "CardModels");
            return inner as System.Collections.IEnumerable ?? Enumerable.Empty<object>();
        }

        private static int? TryGetIntProp(object obj, params string[] names)
        {
            var t = obj.GetType();
            foreach (var n in names)
            {
                var val = t.GetProperty(n, _allInstance)?.GetValue(obj);
                if (val is int i) return i;
                if (val != null && int.TryParse(val.ToString(), out var parsed)) return parsed;
            }
            return null;
        }

        private static int? TryInvokeIntMethod(object obj, params string[] names)
        {
            var t = obj.GetType();
            foreach (var n in names)
            {
                try
                {
                    var m = t.GetMethod(n, _allInstance, null, Type.EmptyTypes, null);
                    if (m == null) continue;
                    var result = m.Invoke(obj, null);
                    if (result is int i) return i;
                    if (result != null && int.TryParse(result.ToString(), out var parsed)) return parsed;
                }
                catch { }
            }
            return null;
        }

        private static string StripRichText(string text) =>
            Regex.Replace(text, @"\[[^\]]+\]", "").Trim();

        // LocString 래퍼에서 실제 문자열 추출 (cards의 Description LocString 등)
        private static string ResolveLocString(object obj)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            if (t.Namespace?.Contains("Localization") == true || t.Name.Contains("LocString"))
            {
                return t.GetProperty("Value")?.GetValue(obj)?.ToString()
                    ?? t.GetProperty("Text")?.GetValue(obj)?.ToString()
                    ?? t.GetProperty("Str")?.GetValue(obj)?.ToString()
                    ?? t.GetProperty("LocalizedText")?.GetValue(obj)?.ToString()
                    ?? t.GetProperty("Localized")?.GetValue(obj)?.ToString()
                    ?? t.GetProperty("Current")?.GetValue(obj)?.ToString();
            }
            return obj.ToString();
        }
    }
}
