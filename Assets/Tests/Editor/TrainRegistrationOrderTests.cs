using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// TrackNetwork.trains(中央登録リスト)の決定性の保証範囲を明文化するEditModeテスト。
//
// 【設計(M2-B.1で確定)】stations/segmentsと同じく、生成側が明示的にAdd/Removeする方式。
// 当初Train.OnEnable/OnDisableでの自己登録を試みたが、EditModeのUnity Test Framework
// 実行下ではAddComponent直後にOnEnableが確実に発火しないことが判明したため(PlayModeでは
// 正常発火するが、テストで検証できないのは致命的)、明示登録方式に統一した。
// 本番コードでの登録箇所: BuildController.DispatchTrain(作成)/RemoveStation・DeleteLine(破棄)、
// SaveLoad.Load(復元)。実プレイでTrainのGameObjectが無効化されることは無い
// (SetActive(false)の呼び出し箇所が存在しないことをgrepで確認済み)ため、
// 「無効化・再有効化」はそもそも起こらないシナリオとして本テストの対象外とする。
//
// 【保証範囲】同一seed・同一入力・同一エンティティ生成順・オーバーロードによる時間切り捨て
// なし、という前提のもとで、TrackNetwork.trainsの登録順は以下で安定する:
//   - 新規ゲームでの複数列車作成(AddComponent<Train>を呼んだ順=Add()を呼んだ順)
//   - 列車の削除→再追加(削除された列車はリストから明示的に除去され、新規追加分は末尾に追加)
//   - セーブ→ロード(SaveLoad.SaveがTrackNetwork.trainsを直接列挙するため、セーブファイル内の
//     順序=セーブ時点の登録順となり、ロード時のAddComponent呼び出し順もそれに追従する)
// 【M2-Cでの更新】セーブv2(GameRandom内部state・列車の走行位置/速度/残りDwell時間・
// 乗車旅客等を保存)導入により、セーブ/ロードをまたいだシミュレーション結果の決定的
// 継続性はSpeedMultiplierEquivalenceTests等と同様の手法でSaveLoadV2Tests.csが別途検証する。
// 本ファイルは引き続き「登録順序」という一つの側面に焦点を当てる
//
// 【Codexレビュー対応】本番の登録・解除経路(BuildController.DispatchTrain/RemoveStation/
// DeleteLine)そのものの回帰検出は、既存のバッチテストServiceTest.cs/StationEditTest.csに
// TrackNetwork.trains.Countの確認を追加することで別途担保している(このファイルは
// TrackNetwork.trainsというデータ構造自体の性質と、SaveLoadとの組み合わせを検証する)。
public class TrainRegistrationOrderTests
{
    const string Key = "railtycoon_save";
    bool hadRealSave;
    string realSaveBackup;

    [SetUp]
    public void SetUp()
    {
        hadRealSave = PlayerPrefs.HasKey(Key);
        if (hadRealSave) realSaveBackup = PlayerPrefs.GetString(Key);
        PlayerPrefs.DeleteKey(Key);
        TrackNetwork.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        EditModeTestHelpers.DestroyWorldRoot();
        TrackNetwork.Clear();
        SaveLoad.suppress = true;

        // Assert失敗やLoad失敗など、テスト本体のどこで止まってもここで必ず実行される
        // (旧実装はテスト末尾で手動復元しており、途中失敗時に実セーブが復元されない懸念があった)
        PlayerPrefs.DeleteKey(Key);
        if (hadRealSave) PlayerPrefs.SetString(Key, realSaveBackup);
        PlayerPrefs.Save();
    }

    static Train MakeTrainStub(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(BuildController.WorldRoot, false);
        var t = go.AddComponent<Train>();
        t.id = ++TrackNetwork.trainIdCounter; // M2-C: id=0の列車はSaveLoad.Saveから除外されるため必須
        TrackNetwork.trains.Add(t); // 本番のBuildController.DispatchTrain等と同じ明示登録
        return t;
    }

    static void DestroyTrainStub(Train t)
    {
        TrackNetwork.trains.Remove(t); // 本番のRemoveStation/DeleteLineと同じ明示解除
        Object.DestroyImmediate(t.gameObject);
    }

    [Test]
    public void NewGame_MultipleTrainCreation_RegistersInCreationOrder()
    {
        var t1 = MakeTrainStub("T1");
        var t2 = MakeTrainStub("T2");
        var t3 = MakeTrainStub("T3");

        Assert.That(TrackNetwork.trains, Is.EqualTo(new List<Train> { t1, t2, t3 }));
    }

    [Test]
    public void DeleteAndReAdd_RemovedEntryGone_NewEntryAppendedAtEnd()
    {
        var t1 = MakeTrainStub("T1");
        var t2 = MakeTrainStub("T2");
        DestroyTrainStub(t2); // 削除(明示Remove)
        var t3 = MakeTrainStub("T3"); // 再追加(末尾に追加)

        Assert.That(TrackNetwork.trains, Is.EqualTo(new List<Train> { t1, t3 }));
    }

    [Test]
    public void SaveLoadRoundTrip_PreservesRegistrationOrder()
    {
        SaveLoad.suppress = false;

        var a = EditModeTestHelpers.MakeStation(new Vector3(-600, 0, 0), 0, 8, 2, 2, "A");
        var b = EditModeTestHelpers.MakeStation(new Vector3(500, 0, 200), 20, 8, 2, 2, "B");
        var c = EditModeTestHelpers.MakeStation(new Vector3(1400, 0, -100), -30, 8, 2, 2, "C");
        EditModeTestHelpers.Connect(a, b);
        EditModeTestHelpers.Connect(b, c);

        // 3本の列車をA/B/Cそれぞれの停車線に配置。個体識別のため異なる編成を使う
        // (formation label=「車種名・両数」がセーブ/ロードを通じて保持されるため、
        // ロード後もどの列車が元のどれに対応するか順序込みで判定できる)
        var fmA = TrainCatalog.Formations[0];
        var fmB = TrainCatalog.Formations[1];
        var fmC = TrainCatalog.Formations[2];
        int ta, tb, tc;
        a.TryReserve(out ta); b.TryReserve(out tb); c.TryReserve(out tc);

        var trainA = MakeTrainStub("TrA");
        trainA.Init(fmA, new List<Station> { a, b }, new List<int> { ta, b.StopTracks[0] });
        var trainB = MakeTrainStub("TrB");
        trainB.Init(fmB, new List<Station> { b, c }, new List<int> { tb, c.StopTracks[0] });
        var trainC = MakeTrainStub("TrC");
        // M2-C: 経路上の隣接駅は実際に線路で結ばれている必要がある(C-Aは未接続、
        // A-B-Cのみ接続済みのため、C→Bの経路にする)
        trainC.Init(fmC, new List<Station> { c, b }, new List<int> { tc, b.StopTracks[0] });

        var beforeOrder = new List<string>();
        foreach (var t in TrackNetwork.trains) beforeOrder.Add(t.fm.Label);
        Assert.That(beforeOrder, Is.EqualTo(new List<string> { fmA.Label, fmB.Label, fmC.Label }), "前提: 作成順どおりに登録されていること");

        SaveLoad.Save();
        Object.DestroyImmediate(BuildController.WorldRoot.gameObject);
        TrackNetwork.Clear();
        SaveLoad.Load();

        var afterOrder = new List<string>();
        foreach (var t in TrackNetwork.trains) afterOrder.Add(t.fm.Label);

        Assert.That(TrackNetwork.trains.Count, Is.EqualTo(3), "3本とも復元されること");
        Assert.That(afterOrder, Is.EqualTo(beforeOrder), "セーブ時点の登録順がロード後も保たれること");
    }
}
