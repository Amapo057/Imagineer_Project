using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 엑셀에서 저장한 CSV 파일을 읽어서 CardData 에셋들을 자동으로 만들거나 갱신하는 에디터 전용 툴.
/// cardClass(소속 클래스), hologramPrefab(홀로그램 프리팹) 같은 참조값은 절대 건드리지 않음 —
/// 그 두 필드는 계속 유니티 인스펙터에서 직접 드래그해서 연결해줘야 함.
///
/// 사용법: 유니티 상단 메뉴 CardGame > CSV로 카드 데이터 가져오기
///
/// CSV 형식 (첫 줄은 헤더, 헤더 이름은 그대로 유지):
/// cardId,cardName,cost,attack,health,cardType,keywords,description
///
/// cardType 칸은 Unit 또는 Spell (한글 "유닛"/"마법"도 인식됨). 비어있으면 Unit으로 처리.
///
/// keywords 칸은 세미콜론(;)으로 여러 개 구분. 한글/영문 둘 다 인식됨:
/// 반격/Counter, 회복/Heal, 전투의함성/Battlecry, 드로우/Draw, 자폭/SelfDestruct, 휘둘기/Cleave
/// 예: "반격;회복" 또는 "Counter;Heal" (능력 없으면 빈 칸)
///
/// 이미 존재하는 카드(같은 cardId)는 수치만 덮어쓰고, 새 cardId는 새 에셋으로 생성함.
/// </summary>
public static class CardCsvImporter
{
    private const string OutputFolder = "Assets/2. Data/Card";

    private static readonly Dictionary<string, CardKeyword> KeywordLookup =
        new Dictionary<string, CardKeyword>(StringComparer.OrdinalIgnoreCase)
    {
        { "반격", CardKeyword.Counter },         { "counter", CardKeyword.Counter },
        { "회복", CardKeyword.Heal },            { "heal", CardKeyword.Heal },
        { "전투의함성", CardKeyword.Battlecry },  { "전투의 함성", CardKeyword.Battlecry }, { "battlecry", CardKeyword.Battlecry },
        { "드로우", CardKeyword.Draw },          { "draw", CardKeyword.Draw },
        { "자폭", CardKeyword.SelfDestruct },    { "selfdestruct", CardKeyword.SelfDestruct },
        { "휘둘기", CardKeyword.Cleave },        { "cleave", CardKeyword.Cleave },
    };

    [MenuItem("CardGame/CSV로 카드 데이터 가져오기")]
    public static void ImportFromCsv()
    {
        string path = EditorUtility.OpenFilePanel("카드 CSV 선택", Application.dataPath, "csv");
        if (string.IsNullOrEmpty(path)) return;

        List<string[]> rows;
        try
        {
            rows = ParseCsv(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogError($"[CardCsvImporter] CSV 읽기 실패: {e.Message}");
            return;
        }

        if (rows.Count < 2)
        {
            Debug.LogWarning("[CardCsvImporter] CSV에 데이터 행이 없음 (헤더만 있거나 빈 파일)");
            return;
        }

        string[] header = rows[0];
        int idxId = Array.IndexOf(header, "cardId");
        int idxName = Array.IndexOf(header, "cardName");
        int idxCost = Array.IndexOf(header, "cost");
        int idxAtk = Array.IndexOf(header, "attack");
        int idxHp = Array.IndexOf(header, "health");
        int idxType = Array.IndexOf(header, "cardType");
        int idxKeywords = Array.IndexOf(header, "keywords");
        int idxDesc = Array.IndexOf(header, "description");

        if (idxId < 0 || idxName < 0 || idxCost < 0 || idxAtk < 0 || idxHp < 0)
        {
            Debug.LogError("[CardCsvImporter] 헤더에 cardId, cardName, cost, attack, health 칸이 모두 있어야 함");
            return;
        }

        EnsureFolder(OutputFolder);

        var existing = LoadExistingCardsById(OutputFolder);
        var seenIds = new HashSet<int>();
        int created = 0, updated = 0;

        foreach (var row in rows.Skip(1))
        {
            if (row.Length <= idxId || string.IsNullOrWhiteSpace(row[idxId])) continue;
            if (!int.TryParse(row[idxId].Trim(), out int cardId))
            {
                Debug.LogWarning($"[CardCsvImporter] cardId 파싱 실패, 행 건너뜀: {string.Join(",", row)}");
                continue;
            }

            if (!seenIds.Add(cardId))
            {
                Debug.LogWarning($"[CardCsvImporter] cardId {cardId} 이(가) CSV 안에서 중복됨 — 나중 행 값으로 덮어씀");
            }

            string cardName = row[idxName].Trim();

            bool isNew = !existing.TryGetValue(cardId, out CardData card);
            if (isNew)
            {
                card = ScriptableObject.CreateInstance<CardData>();
                created++;
            }
            else
            {
                updated++;
            }

            card.cardId = cardId;
            card.cardName = cardName;
            card.cardType = idxType >= 0 ? ParseCardType(row, idxType) : CardType.Unit;
            card.cost = ParseIntSafe(row, idxCost);
            card.attack = ParseIntSafe(row, idxAtk);
            card.health = ParseIntSafe(row, idxHp);
            card.keywords = idxKeywords >= 0 ? ParseKeywords(row, idxKeywords) : CardKeyword.None;
            card.description = (idxDesc >= 0 && idxDesc < row.Length) ? row[idxDesc] : "";

            if (isNew)
            {
                string safeName = string.IsNullOrEmpty(cardName) ? "card" : cardName;
                string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{OutputFolder}/{cardId:00}_{safeName}.asset");
                AssetDatabase.CreateAsset(card, assetPath);
                existing[cardId] = card;
            }
            else
            {
                EditorUtility.SetDirty(card);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[CardCsvImporter] 완료 — 새로 생성 {created}장, 기존 갱신 {updated}장 (cardClass / hologramPrefab은 건드리지 않음)");
    }

    private static CardType ParseCardType(string[] row, int idx)
    {
        if (idx >= row.Length || string.IsNullOrWhiteSpace(row[idx])) return CardType.Unit;

        string key = row[idx].Trim();
        if (key.Equals("spell", StringComparison.OrdinalIgnoreCase) || key == "마법") return CardType.Spell;
        if (key.Equals("unit", StringComparison.OrdinalIgnoreCase) || key == "유닛") return CardType.Unit;

        Debug.LogWarning($"[CardCsvImporter] 인식하지 못한 cardType: \"{key}\" — Unit으로 처리");
        return CardType.Unit;
    }

    private static int ParseIntSafe(string[] row, int idx)
    {
        if (idx < 0 || idx >= row.Length) return 0;
        return int.TryParse(row[idx].Trim(), out int v) ? v : 0;
    }

    private static CardKeyword ParseKeywords(string[] row, int idx)
    {
        if (idx >= row.Length || string.IsNullOrWhiteSpace(row[idx])) return CardKeyword.None;

        CardKeyword result = CardKeyword.None;
        var parts = row[idx].Split(new[] { ';', ',', '/' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in parts)
        {
            string key = raw.Trim();
            if (string.IsNullOrEmpty(key)) continue;

            if (KeywordLookup.TryGetValue(key, out var kw))
            {
                result |= kw;
            }
            else
            {
                Debug.LogWarning($"[CardCsvImporter] 인식하지 못한 키워드: \"{key}\" (건너뜀)");
            }
        }
        return result;
    }

    private static Dictionary<int, CardData> LoadExistingCardsById(string folder)
    {
        var result = new Dictionary<int, CardData>();
        var guids = AssetDatabase.FindAssets("t:CardData", new[] { folder });
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var card = AssetDatabase.LoadAssetAtPath<CardData>(path);
            if (card != null) result[card.cardId] = card;
        }
        return result;
    }

    private static void EnsureFolder(string assetFolderPath)
    {
        // "Assets/2. Data/Card" 처럼 중첩된 경로를 한 단계씩 만들어줌
        var parts = assetFolderPath.Split('/');
        string current = parts[0]; // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }

    // 쉼표로 구분하되, 따옴표(")로 감싼 칸 안의 쉼표/줄바꿈은 그대로 유지하는 간단한 CSV 파서.
    // 엑셀에서 "다른 이름으로 저장 > CSV(쉼표로 분리)" 했을 때 나오는 형식을 그대로 처리함.
    private static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var field = new System.Text.StringBuilder();
        var current = new List<string>();
        bool inQuotes = false;

        // 엑셀이 UTF-8로 저장할 때 붙이는 BOM 제거
        if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    current.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    current.Add(field.ToString());
                    field.Clear();
                    if (current.Any(s => !string.IsNullOrWhiteSpace(s)))
                        rows.Add(current.ToArray());
                    current = new List<string>();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || current.Count > 0)
        {
            current.Add(field.ToString());
            if (current.Any(s => !string.IsNullOrWhiteSpace(s)))
                rows.Add(current.ToArray());
        }

        return rows;
    }
}
