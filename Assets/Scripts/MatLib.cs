using System.Collections.Generic;
using UnityEngine;

// Resources/Materials からマテリアルを引く(WebGLのシェーダーストリッピング対策で
// 実行時生成ではなくアセット化したものを使う。SceneSetup.CreateAll が生成)
public static class MatLib
{
    static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
    static readonly Dictionary<string, Material> tintedCache = new Dictionary<string, Material>();
    static Font font;
    static Material fontDepth;

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

    // TextMeshの既定マテリアル(GUI/Text Shader)は **ZTest Always**。地図の駅名や
    // 番線番号は手前に出したいのでそれでよいが、実景に置く文字(ホームの駅名標・
    // 停車位置目標)まで手前の物体を突き抜けて描かれる。運転士目線にしたところ、
    // 運転台の上に停目の数字が浮いた(2026-07-27)。
    // 奥行きを見る自前のシェーダ(RailTycoon/TextDepth)に差し替えて使う
    public static Material JpFontDepth
    {
        get
        {
            if (fontDepth == null)
            {
                fontDepth = new Material(Get("TextDepth")) { name = "JpFontDepth" };
                // 動的フォントはアトラスを作り直すことがある。作り直されたら差し替える
                // (放っておくと全ての文字が化ける)
                Font.textureRebuilt += OnFontTextureRebuilt;
            }
            fontDepth.mainTexture = JpFont.material.mainTexture;
            return fontDepth;
        }
    }

    static void OnFontTextureRebuilt(Font f)
    {
        if (fontDepth != null && f == JpFont) fontDepth.mainTexture = f.material.mainTexture;
    }
}
