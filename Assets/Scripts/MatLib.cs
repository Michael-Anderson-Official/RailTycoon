using System.Collections.Generic;
using UnityEngine;

// Resources/Materials からマテリアルを引く(WebGLのシェーダーストリッピング対策で
// 実行時生成ではなくアセット化したものを使う。SceneSetup.CreateAll が生成)
public static class MatLib
{
    static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
    static readonly Dictionary<string, Material> tintedCache = new Dictionary<string, Material>();
    static Font font;

    public static Material Get(string name)
    {
        Material m;
        if (cache.TryGetValue(name, out m) && m != null) return m;
        m = Resources.Load<Material>("Materials/" + name);
        if (m == null) Debug.LogError("MatLib: material not found: " + name);
        cache[name] = m;
        return m;
    }

    public static Material Tinted(string baseName, Color c)
    {
        // 駅プレビューの再構築や列車追加のたびにnative Materialを生成すると、
        // GameObjectだけを破棄してもMaterialが残り続ける。色は全て定義済みの少数種類
        // なので、基材+RGBAごとに共有して編集を繰り返しても割当数を増やさない。
        string key = baseName + ":" + ColorUtility.ToHtmlStringRGBA(c);
        if (tintedCache.TryGetValue(key, out var cached) && cached != null) return cached;
        var m = new Material(Get(baseName));
        m.color = c;
        tintedCache[key] = m;
        return m;
    }

    public static Font JpFont
    {
        get
        {
            if (font == null) font = Resources.Load<Font>("NotoSansJP");
            return font;
        }
    }
}
