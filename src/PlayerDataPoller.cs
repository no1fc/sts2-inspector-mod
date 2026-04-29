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
            _dumpedTypes.Clear();   // 씬 재로드마다 덤프 초기화 (static이라 재시작해도 유지되므로)

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

            if (_dumpedTypes.Add("player:" + player.GetType().FullName))
                DumpStringProperties(player, "[Inspector] PlayerProps");

            var character = GetProp(player, "Character");
            snap.CharacterName = character != null
                ? (ResolveLocString(GetProp(character, "Title"))
                   ?? ResolveLocString(GetProp(character, "Name"))
                   ?? "")
                : "";

            snap.PlayerName = GetProp(player, "DisplayName")?.ToString()
                           ?? GetProp(player, "Name")?.ToString()
                           ?? GetProp(player, "Username")?.ToString()
                           ?? GetProp(player, "SteamName")?.ToString()
                           ?? GetProp(player, "PlayerName")?.ToString()
                           ?? snap.CharacterName
                           ?? playerId;

            var pilesRaw = GetProp(player, "Piles");
            var drawPile = FindDrawPile(pilesRaw);
            var hand     = FindHandPile(pilesRaw);
            var relics   = GetProp(player, "Relics");
            var potions  = GetProp(player, "Potions");

            snap.DeckCardIds = ExtractTitles(drawPile);
            snap.RelicIds    = ExtractTitles(relics);
            snap.PotionIds   = ExtractTitles(potions);
            snap.HandCardIds = ExtractTitles(hand);
            var strength  = ExtractStrengthValue(player);
            var dexterity = ExtractDexterityValue(player);
            snap.HandCardEntries = ExtractHandCardEntries(hand, strength, dexterity);
            snap.Buffs = ExtractPowers(player);

            snap.NameToDescription = new Dictionary<string, string>();
            ExtractDescriptions(drawPile, snap.NameToDescription);
            ExtractDescriptions(relics,   snap.NameToDescription);
            ExtractDescriptions(potions,  snap.NameToDescription);
            ExtractDescriptions(hand,     snap.NameToDescription);

            snap.NameToImageKey = new Dictionary<string, string>();
            ExtractImageKeys(drawPile, snap.NameToImageKey);
            ExtractImageKeys(relics,   snap.NameToImageKey);
            ExtractImageKeys(potions,  snap.NameToImageKey);
            ExtractImageKeys(hand,     snap.NameToImageKey);

            snap.NameToStats = new Dictionary<string, string>();
            ExtractStats(drawPile, snap.NameToStats);
            ExtractStats(relics,   snap.NameToStats);
            ExtractStats(potions,  snap.NameToStats);
            ExtractStats(hand,     snap.NameToStats);

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

        // 타입명 기준으로 덤프 여부 추적 — 게임 재시작마다 리셋되어 첫 틱에 항상 덤프
        private static readonly HashSet<string> _dumpedTypes = new();

        private static void ExtractDescriptions(object collection, Dictionary<string, string> dict)
        {
            if (collection == null) return;

            bool firstItem = true;
            foreach (var item in UnwrapCollection(collection))
            {
                if (item == null) continue;

                var name = ResolveDisplayName(item);
                if (dict.ContainsKey(name)) { firstItem = false; continue; }

                // 1순위: HoverTip (단수) — 유물/포션
                // 카드: HoverTip 없음 → HoverTips (복수 IEnumerable) 에서 Title 일치 항목 탐색
                var hoverTip = GetProp(item, "HoverTip") ?? FindHoverTipViaInterface(item);
                if (hoverTip == null)
                {
                    var hoverTipsObj = GetProp(item, "HoverTips");
                    if (hoverTipsObj is System.Collections.IEnumerable htList)
                    {
                        object firstHt = null;
                        foreach (var ht in htList)
                        {
                            if (ht == null) continue;
                            firstHt ??= ht;
                            var htTitle = GetProp(ht, "Title")?.ToString();
                            if (htTitle == name) { hoverTip = ht; break; }
                        }
                        hoverTip ??= firstHt;
                    }
                }

                if (hoverTip != null)
                {
                    if (firstItem && _dumpedTypes.Add(item.GetType().FullName))
                        DumpHoverTip(hoverTip, name);

                    string htDesc = null;
                    foreach (var field in new[] { "Description", "Body", "Subtitle", "Detail", "Text", "Content", "Info" })
                    {
                        var v = GetProp(hoverTip, field)?.ToString();
                        if (!string.IsNullOrEmpty(v)) { htDesc = v; break; }
                    }

                    if (!string.IsNullOrEmpty(htDesc))
                    {
                        dict[name] = StripRichText(htDesc);
                        GD.Print($"[Inspector] Desc (HoverTip): {name} → '{dict[name]}'");
                        firstItem = false;
                        continue;
                    }
                }
                else if (firstItem && _dumpedTypes.Add(item.GetType().FullName))
                {
                    DumpStringProperties(item, name);
                }

                // 2순위: 직접 메서드/프로퍼티 탐색
                object descObj = GetProp(item, "DynamicDescription")
                              ?? GetProp(item, "Description")
                              ?? GetProp(item, "RawDescription")
                              ?? GetProp(item, "CardDescription")
                              ?? GetProp(item, "CardText")
                              ?? GetProp(item, "LocalizedDescription")
                              ?? GetProp(item, "DescriptionText")
                              ?? GetProp(item, "FlavorText");

                if (descObj == null)
                {
                    try { descObj = item.GetType().GetMethod("GetDescription", _allInstance)?.Invoke(item, null); }
                    catch { }
                }

                // DLL 분석에서 발견된 GetCardText() 메서드 시도
                if (descObj == null)
                {
                    try { descObj = item.GetType().GetMethod("GetCardText", _allInstance)?.Invoke(item, null); }
                    catch { }
                }

                // 필드 기반 탐색 — Description이 property가 아닌 field로 저장된 경우
                if (descObj == null)
                {
                    foreach (var f in item.GetType().GetFields(_allInstance))
                    {
                        if (f.Name.IndexOf("desc", StringComparison.OrdinalIgnoreCase) < 0 &&
                            f.Name.IndexOf("text", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        try
                        {
                            var val = f.GetValue(item);
                            if (val != null) { descObj = val; break; }
                        }
                        catch { }
                    }
                }

                // 서브 오브젝트 탐색 — CanonicalInstance 우선 (CardModel / RelicModel / PotionModel 패턴)
                if (descObj == null)
                {
                    var sub = GetProp(item, "CanonicalInstance")
                           ?? GetProp(item, "CardData")
                           ?? GetProp(item, "Data")
                           ?? GetProp(item, "Model");
                    if (sub != null)
                    {
                        descObj = GetProp(sub, "DynamicDescription")
                               ?? GetProp(sub, "Description")
                               ?? GetProp(sub, "RawDescription");

                        // sub 의 GetCardText() 메서드
                        if (descObj == null)
                        {
                            try { descObj = sub.GetType().GetMethod("GetCardText", _allInstance)?.Invoke(sub, null); } catch { }
                        }

                        // sub 의 HoverTip (단수)
                        if (descObj == null)
                        {
                            var subHt = GetProp(sub, "HoverTip");
                            if (subHt != null)
                            {
                                foreach (var f in new[] { "Description", "Body", "Detail", "Text" })
                                {
                                    var v = GetProp(subHt, f)?.ToString();
                                    if (!string.IsNullOrEmpty(v)) { descObj = v; break; }
                                }
                            }
                        }

                        // sub 의 HoverTips (복수)
                        if (descObj == null)
                        {
                            var subHts = GetProp(sub, "HoverTips");
                            if (subHts is System.Collections.IEnumerable subHtList)
                            {
                                object firstSubHt = null;
                                foreach (var ht in subHtList)
                                {
                                    if (ht == null) continue;
                                    firstSubHt ??= ht;
                                    if (GetProp(ht, "Title")?.ToString() == name) { firstSubHt = ht; break; }
                                }
                                if (firstSubHt != null)
                                {
                                    foreach (var f in new[] { "Description", "Body", "Detail", "Text" })
                                    {
                                        var v = GetProp(firstSubHt, f)?.ToString();
                                        if (!string.IsNullOrEmpty(v)) { descObj = v; break; }
                                    }
                                }
                            }
                        }

                        // 진단: descObj가 null이거나 string으로 해석 불가면 한 번만 덤프
                        if (string.IsNullOrEmpty(ResolveLocString(descObj))
                            && firstItem && _dumpedTypes.Add("CI:" + sub.GetType().FullName))
                            DumpStringProperties(sub, $"CanonicalInstance of {name}");
                    }
                }

                var desc = StripRichText(ResolveLocString(descObj) ?? "");

                // 마지막 수단: Godot TranslationServer 직접 조회
                // HoverTip.Id 형식이 "relics.CRACKED_CORE.description" 이므로
                // 카드는 "cards.STRIKE_DEFECT.description" 형식으로 시도
                if (string.IsNullOrEmpty(desc))
                {
                    var rawKey = GetProp(item, "Key")?.ToString()
                              ?? GetProp(item, "CardModelId")?.ToString()
                              ?? GetProp(item, "Id")?.ToString();
                    if (!string.IsNullOrEmpty(rawKey))
                    {
                        // "card_strike_defect" → "STRIKE_DEFECT"
                        // "CARD.STRIKE_DEFECT"  → "STRIKE_DEFECT"  (ModelId.ToString() 패턴)
                        var locId = rawKey;
                        if (locId.StartsWith("card_", StringComparison.OrdinalIgnoreCase)) locId = locId[5..];
                        else if (locId.StartsWith("CARD.", StringComparison.Ordinal))   locId = locId[5..];
                        else if (locId.StartsWith("RELIC.", StringComparison.Ordinal))  locId = locId[6..];
                        else if (locId.StartsWith("POTION.", StringComparison.Ordinal)) locId = locId[7..];
                        locId = locId.ToUpperInvariant();

                        foreach (var fmt in new[] {
                            $"cards.{locId}.description",
                            $"card.{locId}.description",
                            $"{locId}.description"
                        })
                        {
                            var translated = Godot.TranslationServer.Translate(fmt);
                            if (!string.IsNullOrEmpty(translated) && translated != fmt)
                            {
                                desc = StripRichText(translated);
                                GD.Print($"[Inspector] Desc (TranslationServer '{fmt}'): {name} → '{desc}'");
                                break;
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(desc))
                    dict[name] = desc;
                else
                    GD.Print($"[Inspector] Desc FAIL: {name} (id={GetProp(item, "Id")}, descObj={descObj?.GetType().Name ?? "null"})");

                firstItem = false;
            }
        }

        private static object FindHoverTipViaInterface(object item)
        {
            foreach (var iface in item.GetType().GetInterfaces())
            {
                try
                {
                    var htProp = iface.GetProperty("HoverTip");
                    var getter = htProp?.GetGetMethod(true);
                    if (getter == null) continue;
                    var map = item.GetType().GetInterfaceMap(iface);
                    var idx = Array.IndexOf(map.InterfaceMethods, getter);
                    if (idx < 0) continue;
                    var result = map.TargetMethods[idx].Invoke(item, null);
                    if (result != null)
                    {
                        GD.Print($"[Inspector] HoverTip via iface {iface.Name}: {result.GetType().FullName}");
                        return result;
                    }
                }
                catch { }
            }
            return null;
        }

        private static void DumpHoverTip(object hoverTip, string itemName)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[Inspector] HoverTipDump for '{itemName}' ({hoverTip.GetType().FullName}):");
            foreach (var p in hoverTip.GetType().GetProperties(_allInstance))
            {
                try
                {
                    var val = p.GetValue(hoverTip);
                    if (val == null) continue;
                    var raw = val.ToString() ?? "";
                    if (raw == p.PropertyType.FullName) continue;
                    if (raw.Length > 120) raw = raw[..120] + "…";
                    sb.AppendLine($"  [{p.PropertyType.Name}] {p.Name} = {raw}");
                }
                catch { }
            }
            GD.Print(sb.ToString());
        }

        private static void DumpStringProperties(object item, string itemName)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[Inspector] PropDump for '{itemName}' ({item.GetType().FullName}):");

            sb.AppendLine("  -- Properties --");
            foreach (var p in item.GetType().GetProperties(_allInstance))
            {
                try
                {
                    var val = p.GetValue(item);
                    if (val == null)
                    {
                        if (p.PropertyType == typeof(string) || p.PropertyType.Name.Contains("LocString"))
                            sb.AppendLine($"  [{p.PropertyType.Name}] {p.Name} = NULL");
                        continue;
                    }
                    var raw = val.ToString() ?? "";
                    if (raw == p.PropertyType.FullName) continue;
                    if (raw.Length > 120) raw = raw[..120] + "…";
                    sb.AppendLine($"  [{p.PropertyType.Name}] {p.Name} = {raw}");
                }
                catch { }
            }

            sb.AppendLine("  -- Fields --");
            foreach (var f in item.GetType().GetFields(_allInstance))
            {
                try
                {
                    var val = f.GetValue(item);
                    if (val == null)
                    {
                        if (f.FieldType == typeof(string) || f.FieldType.Name.Contains("LocString"))
                            sb.AppendLine($"  [FIELD {f.FieldType.Name}] {f.Name} = NULL");
                        continue;
                    }
                    var raw = val.ToString() ?? "";
                    if (raw == f.FieldType.FullName) continue;
                    if (raw.Length > 120) raw = raw[..120] + "…";
                    sb.AppendLine($"  [FIELD {f.FieldType.Name}] {f.Name} = {raw}");
                }
                catch { }
            }

            sb.AppendLine("  -- Interfaces (HoverTip/Description) --");
            foreach (var iface in item.GetType().GetInterfaces())
            {
                try
                {
                    bool hasHt = iface.GetProperty("HoverTip") != null;
                    bool hasDe = iface.GetProperty("Description") != null;
                    if (hasHt || hasDe)
                        sb.AppendLine($"  [IFACE] {iface.FullName} HoverTip={hasHt} Description={hasDe}");
                }
                catch { }
            }

            GD.Print(sb.ToString());
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

            int? damage = null, block = null, cost = null;

            // CanonicalInstance를 일찍 추출해 sub에 포함
            var ci  = GetProp(item, "CanonicalInstance");
            var sub = GetProp(item, "CardData") ?? GetProp(item, "Data") ?? GetProp(item, "Model") ?? ci;

            cost = TryGetIntProp(item, "CurrentCost", "CurrentEnergyCost",
                                      "CanonicalEnergyCost", "BaseCost", "BaseEnergyCost", "EnergyCost", "Cost");
            if (cost == null && sub != null)
                cost = TryGetIntProp(sub, "CurrentCost", "CurrentEnergyCost",
                                         "CanonicalEnergyCost", "BaseCost", "EnergyCost", "Cost");

            // CanonicalVars / DynamicVars 열거
            var varSet = GetProp(item, "CanonicalVars") ?? GetProp(item, "DynamicVars") ?? GetProp(item, "Vars");
            if (varSet == null && ci != null)
                varSet = GetProp(ci, "CanonicalVars") ?? GetProp(ci, "DynamicVars") ?? GetProp(ci, "Vars");

            if (varSet is System.Collections.IEnumerable canonVars)
            {
                int? calculationBase = null;
                bool hasCalculatedDamage = false, hasCalculatedBlock = false;
                foreach (var v in canonVars)
                {
                    if (v == null) continue;
                    object valueObj = v;
                    var kvValue = v.GetType().GetProperty("Value")?.GetValue(v);
                    if (kvValue != null && kvValue is not int && kvValue is not string)
                        valueObj = kvValue;

                    var key = GetProp(v, "Name")?.ToString()
                           ?? GetProp(v, "Key")?.ToString()
                           ?? GetProp(v, "Id")?.ToString();
                    var val = TryGetIntProp(valueObj, "CanonicalValue", "IntValue", "CurrentValue", "Value", "BaseValue");
                    if (key == null || val == null) continue;
                    var ku = key.ToUpperInvariant();

                    if      (ku is "D" or "DAMAGE" or "DMG" or "ATK" or "ATTACK")    damage ??= val;
                    else if (ku is "B" or "BLOCK" or "DEF" or "DEFENSE" or "SHIELD") block  ??= val;
                    else if (ku == "CALCULATIONBASE")  calculationBase     = val;
                    else if (ku == "CALCULATEDDAMAGE") hasCalculatedDamage = true;
                    else if (ku == "CALCULATEDBLOCK")  hasCalculatedBlock  = true;
                }
                damage ??= hasCalculatedDamage ? calculationBase : null;
                block  ??= hasCalculatedBlock  ? calculationBase : null;
            }

            // 직접 프로퍼티/메서드 fallback
            damage ??= TryGetIntProp(item, "BaseDamage", "DamageAmount", "Damage");
            block  ??= TryGetIntProp(item, "BaseBlock",  "BlockAmount",  "Block");
            damage ??= TryInvokeIntMethod(item, "GetBaseDamage", "GetDamage", "GetCalculatedDamage");
            block  ??= TryInvokeIntMethod(item, "GetBaseBlock",  "GetBlock");
            if (sub != null)
            {
                damage ??= TryGetIntProp(sub, "BaseDamage", "DamageAmount", "Damage");
                block  ??= TryGetIntProp(sub, "BaseBlock",  "BlockAmount",  "Block");
                damage ??= TryInvokeIntMethod(sub, "GetBaseDamage", "GetDamage", "GetCalculatedDamage");
                block  ??= TryInvokeIntMethod(sub, "GetBaseBlock",  "GetBlock");
            }

            // 효과 컬렉션 fallback
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

        // ── 버프/디버프(Powers) 추출 ──────────────────────────────────────────

        private static List<BuffEntry> ExtractPowers(object player)
        {
            var result = new List<BuffEntry>();

            object powers = GetProp(player, "Powers")
                         ?? GetProp(player, "Buffs")
                         ?? GetProp(player, "StatusEffects");
            if (powers == null)
            {
                var ch = GetProp(player, "Character");
                if (ch != null)
                    powers = GetProp(ch, "Powers") ?? GetProp(ch, "Buffs");
            }
            if (powers is not System.Collections.IEnumerable powerList) return result;

            foreach (var power in powerList)
            {
                if (power == null) continue;

                var isVisible = GetProp(power, "IsVisible") ?? GetProp(power, "IsVisibleInternal");
                if (isVisible is bool b && !b) continue;

                var name = ResolveDisplayName(power);
                if (string.IsNullOrEmpty(name)) continue;

                var amount = TryGetIntProp(power, "Amount", "DisplayAmount", "StackCount",
                                                  "Stacks", "CurrentValue") ?? 0;

                var typeStr = GetProp(power, "Type")?.ToString() ?? "";
                bool isDebuff = typeStr.IndexOf("debuff", StringComparison.OrdinalIgnoreCase) >= 0;

                result.Add(new BuffEntry(name, amount, isDebuff));
                GD.Print($"[Inspector] Power: {name} amt={amount} debuff={isDebuff}");
            }
            return result;
        }

        // ── 강도(Strength) / 민첩(Dexterity) 버프 추출 ──────────────────────────

        private static int ExtractDexterityValue(object player)
        {
            foreach (var collName in new[] { "Buffs", "Powers", "StatusEffects", "ActiveEffects" })
            {
                var coll = GetProp(player, collName);
                if (coll is not System.Collections.IEnumerable list) continue;
                foreach (var buff in list)
                {
                    if (buff == null) continue;
                    var key = (GetProp(buff, "Key") ?? GetProp(buff, "Id")
                            ?? GetProp(buff, "Name") ?? GetProp(buff, "Type"))?.ToString() ?? "";
                    if (key.IndexOf("dexterity", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var val = TryGetIntProp(buff, "Value", "Amount", "Stacks", "CurrentValue", "Magnitude");
                    if (val.HasValue)
                    {
                        GD.Print($"[Inspector] Dexterity: {val.Value} via {collName}.{key}");
                        return val.Value;
                    }
                }
            }
            var ch = GetProp(player, "Character");
            if (ch != null)
            {
                var v = TryGetIntProp(ch, "Dexterity", "DexterityValue");
                if (v.HasValue) return v.Value;
            }
            return 0;
        }

        private static int ExtractStrengthValue(object player)
        {
            foreach (var collName in new[] { "Buffs", "Powers", "StatusEffects", "ActiveEffects" })
            {
                var coll = GetProp(player, collName);
                if (coll is not System.Collections.IEnumerable list) continue;
                foreach (var buff in list)
                {
                    if (buff == null) continue;
                    var key = (GetProp(buff, "Key") ?? GetProp(buff, "Id")
                            ?? GetProp(buff, "Name") ?? GetProp(buff, "Type"))?.ToString() ?? "";
                    if (key.IndexOf("strength", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var val = TryGetIntProp(buff, "Value", "Amount", "Stacks", "CurrentValue", "Magnitude");
                    if (val.HasValue)
                    {
                        GD.Print($"[Inspector] Strength: {val.Value} via {collName}.{key}");
                        return val.Value;
                    }
                }
            }
            var ch = GetProp(player, "Character");
            if (ch != null)
            {
                var v = TryGetIntProp(ch, "Strength", "StrengthValue");
                if (v.HasValue) return v.Value;
            }
            return 0;
        }

        private static List<HandCardEntry> ExtractHandCardEntries(object hand, int strength, int dexterity)
        {
            var result = new List<HandCardEntry>();
            if (hand == null) return result;

            foreach (var item in UnwrapCollection(hand))
            {
                if (item == null) continue;
                var name = ResolveDisplayName(item);

                // CanonicalInstance를 일찍 추출해 모든 fallback에서 재사용
                var ci  = GetProp(item, "CanonicalInstance");
                var sub = GetProp(item, "CardData") ?? GetProp(item, "Data") ?? GetProp(item, "Model") ?? ci;

                // 진단 덤프 — 처음 보는 카드 타입이면 프로퍼티 목록 출력
                if (_dumpedTypes.Add("hand:" + item.GetType().FullName))
                {
                    DumpStringProperties(item, $"HandCard:{name}");
                    if (ci != null) DumpStringProperties(ci, $"CanonicalInstance of {name}");
                }

                // ── 코스트 ───────────────────────────────────────────────────
                int? cost = TryGetIntProp(item,
                    "CurrentCost", "CurrentEnergyCost", "ModifiedCost",
                    "CanonicalEnergyCost", "BaseCost", "BaseEnergyCost", "EnergyCost", "Cost");
                if (cost == null && sub != null)
                    cost = TryGetIntProp(sub,
                        "CurrentCost", "CurrentEnergyCost",
                        "CanonicalEnergyCost", "BaseCost", "EnergyCost", "Cost");

                // ── VarSet 획득 ──────────────────────────────────────────────
                var varSet = GetProp(item, "CanonicalVars") ?? GetProp(item, "DynamicVars") ?? GetProp(item, "Vars");
                if (varSet == null && ci != null)
                    varSet = GetProp(ci, "CanonicalVars") ?? GetProp(ci, "DynamicVars") ?? GetProp(ci, "Vars");

                // ── 데미지 + 방어 추출 (단일 varSet 패스, 실시간/정적 구분) ────
                int? baseDamage = null;
                int? baseBlock  = null;
                bool realTimeDamage = false; // CanonicalValue 사용 시 → 강도 미추가
                bool realTimeBlock  = false; // CanonicalValue 사용 시 → 민첩 미추가

                if (varSet is System.Collections.IEnumerable canonVarsAll)
                {
                    // varSet 엔트리별 CanonicalValue(실시간)와 BaseValue(초기) 분리 추적
                    int? dmgCanonical = null, dmgBase = null;
                    int? blkCanonical = null, blkBase = null;
                    int? calculationBase = null;
                    int? calcDmgCanonical = null, calcBlkCanonical = null;
                    bool hasCalculatedDamage = false, hasCalculatedBlock = false;

                    bool dumpVars = _dumpedTypes.Add("vars:" + item.GetType().FullName);
                    var sbVars = dumpVars ? new System.Text.StringBuilder($"[Inspector] VarSet '{name}': ") : null;

                    foreach (var v in canonVarsAll)
                    {
                        if (v == null) continue;
                        object valueObj = v;
                        var kvVal = v.GetType().GetProperty("Value")?.GetValue(v);
                        if (kvVal != null && kvVal is not int && kvVal is not string) valueObj = kvVal;
                        var key = GetProp(v, "Name")?.ToString() ?? GetProp(v, "Key")?.ToString() ?? GetProp(v, "Id")?.ToString();

                        var canonVal = TryGetIntProp(valueObj, "CanonicalValue", "CurrentValue");
                        var baseVal  = TryGetIntProp(valueObj, "BaseValue");
                        if (dumpVars)
                            sbVars!.Append($"{key}(rt={canonVal} base={baseVal}) ");

                        var anyVal = canonVal ?? baseVal
                                  ?? TryGetIntProp(valueObj, "IntValue", "Value");
                        if (key == null) continue;
                        var ku = key.ToUpperInvariant();

                        if (ku is "D" or "DAMAGE" or "DMG" or "ATK" or "ATTACK")
                        {
                            if (dmgCanonical == null && canonVal > 0) dmgCanonical = canonVal;
                            if (dmgBase == null && (baseVal ?? anyVal) > 0) dmgBase = baseVal ?? anyVal;
                        }
                        else if (ku is "B" or "BLOCK" or "DEF" or "DEFENSE" or "SHIELD")
                        {
                            if (blkCanonical == null && canonVal > 0) blkCanonical = canonVal;
                            if (blkBase == null && (baseVal ?? anyVal) > 0) blkBase = baseVal ?? anyVal;
                        }
                        else if (ku == "CALCULATIONBASE")
                        {
                            calculationBase = (baseVal ?? anyVal);
                        }
                        else if (ku == "CALCULATEDDAMAGE")
                        {
                            hasCalculatedDamage = true;
                            if (canonVal > 0) calcDmgCanonical = canonVal;
                        }
                        else if (ku == "CALCULATEDBLOCK")
                        {
                            hasCalculatedBlock = true;
                            if (canonVal > 0) calcBlkCanonical = canonVal;
                        }
                    }

                    if (dumpVars) GD.Print(sbVars!.ToString());

                    // 데미지: 실시간(CanonicalValue) 우선, 없으면 초기값
                    if (dmgCanonical > 0)          { baseDamage = dmgCanonical; realTimeDamage = true; }
                    else if (dmgBase > 0)            baseDamage = dmgBase;
                    // CalculationBase + CalculatedDamage 패턴 (영혼 폭풍 등)
                    else if (hasCalculatedDamage)
                    {
                        if (calcDmgCanonical > 0)  { baseDamage = calcDmgCanonical; realTimeDamage = true; }
                        else                          baseDamage = calculationBase;
                    }

                    // 방어: 동일 패턴
                    if (blkCanonical > 0)           { baseBlock = blkCanonical; realTimeBlock = true; }
                    else if (blkBase > 0)              baseBlock = blkBase;
                    else if (hasCalculatedBlock)
                    {
                        if (calcBlkCanonical > 0)  { baseBlock = calcBlkCanonical; realTimeBlock = true; }
                        else                          baseBlock = calculationBase;
                    }
                }

                // 3순위: 직접 프로퍼티/메서드 ('Damage' 단독 제외 — 보너스값 오탐 방지)
                baseDamage ??= TryGetIntProp(item, "BaseDamage", "DamageAmount", "InitialDamage", "DefaultDamage");
                baseDamage ??= TryInvokeIntMethod(item, "GetBaseDamage", "GetDamage", "GetCalculatedDamage");
                baseBlock  ??= TryGetIntProp(item, "BaseBlock", "BlockAmount", "InitialBlock");
                baseBlock  ??= TryInvokeIntMethod(item, "GetBaseBlock", "GetBlock");
                if (sub != null)
                {
                    baseDamage ??= TryGetIntProp(sub, "BaseDamage", "DamageAmount", "InitialDamage", "DefaultDamage");
                    baseDamage ??= TryInvokeIntMethod(sub, "GetBaseDamage", "GetDamage", "GetCalculatedDamage");
                    baseBlock  ??= TryGetIntProp(sub, "BaseBlock", "BlockAmount", "InitialBlock");
                    baseBlock  ??= TryInvokeIntMethod(sub, "GetBaseBlock", "GetBlock");
                }

                // 4순위: Effects/CardEffects/Actions 컬렉션
                var effects = GetProp(item, "Effects") ?? GetProp(item, "CardEffects") ?? GetProp(item, "Actions");
                if (effects is System.Collections.IEnumerable effectList)
                {
                    foreach (var eff in effectList)
                    {
                        if (eff == null) continue;
                        baseDamage ??= TryGetIntProp(eff, "BaseDamage", "DamageAmount", "Amount");
                        baseBlock  ??= TryGetIntProp(eff, "BaseBlock",  "BlockAmount",  "Amount");
                    }
                }

                // 공격 카드에만 강도 적용 (실시간 값은 이미 포함됨 → 미추가)
                var cardTypeStr = GetProp(item, "CardType")?.ToString() ?? GetProp(item, "Type")?.ToString() ?? "";
                bool isAttack = cardTypeStr.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0
                             || cardTypeStr.IndexOf("공격", StringComparison.OrdinalIgnoreCase) >= 0;

                int? effectiveDamage = (baseDamage.HasValue && baseDamage.Value > 0)
                    ? baseDamage.Value + (realTimeDamage ? 0 : (isAttack ? strength : 0)) : null;
                int? effectiveBlock = (baseBlock.HasValue && baseBlock.Value > 0)
                    ? baseBlock.Value + (realTimeBlock ? 0 : dexterity) : null;

                result.Add(new HandCardEntry(name, cost, effectiveDamage, effectiveBlock));
                GD.Print($"[Inspector] HandEntry: {name} cost={cost} dmg={effectiveDamage}(rt={realTimeDamage}) blk={effectiveBlock}(rt={realTimeBlock})");
            }
            return result;
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

        // Piles[] 배열에서 드로우 파일 CardPile 반환 (전투 중에만 존재)
        // "Deck"는 마스터 덱이므로 제외; Hand/Discard/Exhaust/Void 제외 후 남는 pile이 드로우 파일
        private static object FindDrawPile(object pilesRaw)
        {
            if (pilesRaw is not System.Collections.IEnumerable pilesEnum) return null;

            var piles = pilesEnum.Cast<object>().Where(p => p != null).ToList();
            if (piles.Count == 0) return null;

            foreach (var p in piles)
            {
                var t = GetProp(p, "Type")?.ToString();
                if (t is "DrawPile" or "Draw" or "Library") return p;
            }
            foreach (var p in piles)
            {
                var t = GetProp(p, "Type")?.ToString();
                if (t is null or "Deck" or "Hand" or "Discard" or "Exhaust" or "Void") continue;
                return p;
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
            if (obj == null) return null;
            var t = obj.GetType();
            foreach (var n in names)
            {
                try
                {
                    var val = t.GetProperty(n, _allInstance)?.GetValue(obj);
                    if (val == null) continue;
                    if (val is int i)    return i;
                    if (val is float f)  return (int)Math.Round(f);
                    if (val is double d) return (int)Math.Round(d);
                    if (val is long l)   return (int)l;
                    if (int.TryParse(val.ToString(), out var parsed)) return parsed;
                }
                catch { }
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

        private static string StripRichText(string text)
        {
            text = Regex.Replace(text, @"\[[^\]]+\]", "");  // [b], [color=...] 등 rich text 태그
            text = Regex.Replace(text, @"\{[^}]+\}",   ""); // {Damage.diff()} 등 SmartFormat 플레이스홀더
            return Regex.Replace(text, @" {2,}", " ").Trim();
        }

        // LocString 래퍼에서 실제 문자열 추출 — 타입명 검사 없이 모든 오브젝트에 시도
        private static string ResolveLocString(object obj)
        {
            if (obj == null) return null;
            if (obj is string s) return string.IsNullOrEmpty(s) ? null : s;

            var t = obj.GetType();
            if (t.IsPrimitive) return obj.ToString();

            // 알려진 LocString 프로퍼티명을 타입 무관하게 탐색
            var result = t.GetProperty("Value",         _allInstance)?.GetValue(obj)?.ToString()
                      ?? t.GetProperty("Text",          _allInstance)?.GetValue(obj)?.ToString()
                      ?? t.GetProperty("Str",           _allInstance)?.GetValue(obj)?.ToString()
                      ?? t.GetProperty("LocalizedText", _allInstance)?.GetValue(obj)?.ToString()
                      ?? t.GetProperty("Localized",     _allInstance)?.GetValue(obj)?.ToString()
                      ?? t.GetProperty("Current",       _allInstance)?.GetValue(obj)?.ToString();

            if (!string.IsNullOrEmpty(result)) return result;

            // STS2 LocString: GetRawText() → raw template string (StripRichText가 {placeholder} 제거)
            // GetFormattedText()는 호출 시 STS2가 내부 에러를 로그에 출력하므로 사용 안 함
            var locTable = t.GetProperty("LocTable", _allInstance)?.GetValue(obj)?.ToString();
            if (!string.IsNullOrEmpty(locTable))
            {
                try
                {
                    var raw = t.GetMethod("GetRawText", _allInstance)?.Invoke(obj, null)?.ToString();
                    if (!string.IsNullOrEmpty(raw)) return raw;
                }
                catch { }
            }

            // ToString()이 클래스 전체 이름이 아닐 때만 반환
            var str = obj.ToString();
            return (str != null && str != t.FullName && str != t.Name) ? str : null;
        }
    }
}
