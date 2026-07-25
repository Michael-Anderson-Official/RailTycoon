using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// バッチから -executeMethod WebGLBuild.Run。出力は Builds/WebGL。
// GitHub Pages は .br に Content-Encoding を付けないため decompressionFallback 必須
public static class WebGLBuild
{
    public static void Run()
    {
        SceneSetup.EnsureShaders(); // 実行時生成UIのシェーダーがストリッピングされないよう毎回確認
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.runInBackground = true;

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Main.unity" },
            target = BuildTarget.WebGL,
            locationPathName = "Builds/WebGL",
        });
        if (report.summary.result == BuildResult.Succeeded) PatchHtml("Builds/WebGL");

        Debug.Log($"WebGLBuild: {report.summary.result}, size={report.summary.totalSize / 1024 / 1024}MB, " +
                  $"errors={report.summary.totalErrors}, time={report.summary.totalTime}");
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    static void PatchHtml(string outDir)
    {
        // WebIcons/ のアイコン・manifestをビルド出力へコピー(ビルドごとに再生成されるため)
        var iconsOut = System.IO.Path.Combine(outDir, "icons");
        System.IO.Directory.CreateDirectory(iconsOut);
        foreach (var f in System.IO.Directory.GetFiles("WebIcons", "icon-*.png"))
            System.IO.File.Copy(f, System.IO.Path.Combine(iconsOut, System.IO.Path.GetFileName(f)), true);
        System.IO.File.Copy("WebIcons/manifest.webmanifest",
            System.IO.Path.Combine(outDir, "manifest.webmanifest"), true);
        // iOSはサイト直下の apple-touch-icon.png も探しに行くのでフォールバックを置く
        System.IO.File.Copy("WebIcons/icon-180.png",
            System.IO.Path.Combine(outDir, "apple-touch-icon.png"), true);
        System.IO.File.Copy("WebIcons/icon-180.png",
            System.IO.Path.Combine(outDir, "apple-touch-icon-precomposed.png"), true);

        var htmlPath = System.IO.Path.Combine(outDir, "index.html");
        var html = System.IO.File.ReadAllText(htmlPath);
        html = html.Replace("<html lang=\"en-us\">", "<html lang=\"ja\">");
        html = System.Text.RegularExpressions.Regex.Replace(
            html, "<title>.*?</title>", "<title>レールタイクーン</title>");
        // ?v= はiOSのapple-touch-iconキャッシュ対策(アイコンを変えたら数字を上げる)
        const string headExtra =
            "<link rel=\"icon\" type=\"image/png\" sizes=\"32x32\" href=\"icons/icon-32.png?v=2\">\n" +
            "    <link rel=\"apple-touch-icon\" sizes=\"180x180\" href=\"icons/icon-180.png?v=2\">\n" +
            "    <link rel=\"manifest\" href=\"manifest.webmanifest?v=2\">\n" +
            "    <meta name=\"viewport\" content=\"width=device-width,height=device-height," +
            "initial-scale=1,maximum-scale=1,user-scalable=no,viewport-fit=cover\">\n" +
            "    <meta name=\"theme-color\" content=\"#0e1420\">\n" +
            "    <meta name=\"apple-mobile-web-app-capable\" content=\"yes\">\n" +
            "    <meta name=\"apple-mobile-web-app-status-bar-style\" content=\"default\">\n" +
            "    <meta name=\"apple-mobile-web-app-title\" content=\"レールタイクーン\">\n" +
            // Unity既定テンプレートのdesktop 960x600固定も上書きし、全端末で表示面を使い切る。
            // touch-action:none はSafariによるドラッグ/ピンチの横取りを防ぐ。
            "    <style>\n" +
            "      html,body{width:100%;height:100%;margin:0;overflow:hidden;background:#0e1420;" +
            "overscroll-behavior:none}\n" +
            "      #unity-container{position:fixed!important;inset:0!important;width:100%!important;" +
            "height:100%!important;transform:none!important}\n" +
            "      #unity-canvas{display:block;width:100%!important;height:100%!important;" +
            "touch-action:none;background:#0e1420}\n" +
            "      #unity-footer{display:none!important}\n" +
            "      #unity-loading-bar{top:50%!important;left:50%!important;" +
            "transform:translate(-50%,-50%)!important}\n" +
            "      #unity-warning{position:absolute;left:max(12px,env(safe-area-inset-left));" +
            "right:max(12px,env(safe-area-inset-right));top:max(12px,env(safe-area-inset-top))}\n" +
            "    </style>\n" +
            "    <link rel=\"stylesheet\"";
        html = html.Replace("<link rel=\"stylesheet\"", headExtra);
        System.IO.File.WriteAllText(htmlPath, html);
        Debug.Log("WebGLBuild: html patched + icons injected");
    }
}
