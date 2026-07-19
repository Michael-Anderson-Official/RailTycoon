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
        var htmlPath = System.IO.Path.Combine(outDir, "index.html");
        var html = System.IO.File.ReadAllText(htmlPath);
        html = System.Text.RegularExpressions.Regex.Replace(
            html, "<title>.*?</title>", "<title>レールタイクーン</title>");
        const string headExtra =
            "<meta name=\"apple-mobile-web-app-capable\" content=\"yes\">\n" +
            // touch-action:none がないとSafariがドラッグ/ピンチをページ側ジェスチャーとして横取りする
            "    <style>#unity-canvas{touch-action:none}body{overscroll-behavior:none}</style>\n" +
            "    <link rel=\"stylesheet\"";
        html = html.Replace("<link rel=\"stylesheet\"", headExtra);
        System.IO.File.WriteAllText(htmlPath, html);
        Debug.Log("WebGLBuild: html patched");
    }
}
