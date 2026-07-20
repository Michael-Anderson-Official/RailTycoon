mergeInto(LibraryManager.library, {
  // ブラウザのwindow.promptで駅名を入力させ、UTF8文字列を返す。
  // キャンセル時は空文字を返す(C#側で「変更なし」扱い)。
  RailPromptName: function (defPtr) {
    var def = UTF8ToString(defPtr);
    var r = window.prompt("新しい駅名を入力してください", def);
    if (r === null) r = "";
    var len = lengthBytesUTF8(r) + 1;
    var buf = _malloc(len);
    stringToUTF8(r, buf, len);
    return buf;
  }
});
