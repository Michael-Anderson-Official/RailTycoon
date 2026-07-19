using System.Collections.Generic;
using UnityEngine;

// Resources/Materials からマテリアルを引く(WebGLのシェーダーストリッピング対策で
// 実行時生成ではなくアセット化したものを使う。SceneSetup.CreateAll が生成)
public static class MatLib
{
    static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
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
        var m = new Material(Get(baseName));
        m.color = c;
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
