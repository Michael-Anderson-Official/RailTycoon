using System.Collections.Generic;
using UnityEngine;

// 種別カタログ(今回はラベルと色のみ。速度/運賃は編成依存で共通)
public static class ServiceType
{
    public static readonly string[] Names = { "特急", "急行", "準急", "普通" };
    public static readonly Color[] Colors =
    {
        new Color(0.86f, 0.22f, 0.24f), // 特急 赤
        new Color(0.95f, 0.55f, 0.12f), // 急行 橙
        new Color(0.22f, 0.66f, 0.36f), // 準急 緑
        new Color(0.26f, 0.52f, 0.86f), // 普通 青
    };
    public static int Clamp(int i) => Mathf.Clamp(i, 0, Names.Length - 1);
}

// 運行系統(ダイヤの1系統)。種別+停車駅列+各停車駅の番線を持つ。
// 列車はこの系統に配属され、系統の経路を往復する
public class ServiceLine
{
    public int id;
    public int typeIdx;
    public string name;                 // 空ならAutoName(種別 起点→終点)
    public List<Station> route = new List<Station>();
    public List<int> tracks = new List<int>();

    public string TypeName => ServiceType.Names[ServiceType.Clamp(typeIdx)];
    public Color TypeColor => ServiceType.Colors[ServiceType.Clamp(typeIdx)];

    public string AutoName => route.Count >= 2
        ? TypeName + " " + route[0].stationName + "→" + route[route.Count - 1].stationName
        : TypeName;
    public string DisplayName => string.IsNullOrEmpty(name) ? AutoName : name;

    // この系統に配属中の列車数
    public int TrainCount
    {
        get
        {
            int n = 0;
            foreach (var t in Object.FindObjectsByType<Train>(FindObjectsSortMode.None))
                if (t.lineId == id) n++;
            return n;
        }
    }
}

// 運行系統の台帳
public static class Services
{
    public static readonly List<ServiceLine> lines = new List<ServiceLine>();
    public static int idCounter;

    public static void Clear()
    {
        lines.Clear();
        idCounter = 0;
    }

    public static ServiceLine ById(int id)
    {
        foreach (var l in lines) if (l.id == id) return l;
        return null;
    }
}
