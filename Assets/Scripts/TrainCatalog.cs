using System.Collections.Generic;
using UnityEngine;

// 車種カタログ。編成(車種×両数)の一覧を購入UIに出す
public static class TrainCatalog
{
    public class TrainTypeDef
    {
        public string id;
        public string name;
        public int capPerCar;       // 定員/両
        public float maxSpeedKmh;
        public float accelKmhs;     // 起動加速度 km/h/s(実車値)
        public float decelKmhs;     // 常用最大減速度 km/h/s(実車値)
        public float costPerCarOku; // 億円/両
        public Color body, stripe, front;

        public float Accel => accelKmhs / 3.6f; // m/s²
        public float Decel => decelKmhs / 3.6f;
    }

    public class Formation
    {
        public TrainTypeDef type;
        public int cars;
        public string Label => type.name + "・" + cars + "両";
        public double CostYen => type.costPerCarOku * cars * 1e8;
        public int Capacity => type.capPerCar * cars;
    }

    static Color Hex(string h)
    {
        Color c;
        ColorUtility.TryParseHtmlString("#" + h, out c);
        return c;
    }

    public static readonly TrainTypeDef Keio5000 = new TrainTypeDef
    {
        id = "keio5000", name = "京王5000系", capPerCar = 130, maxSpeedKmh = 110,
        accelKmhs = 3.3f, decelKmhs = 4.0f, costPerCarOku = 1.6f,
        body = Hex("C9CDD2"), stripe = Hex("D6006F"), front = Hex("222A44"),
    };
    public static readonly TrainTypeDef Mei2000 = new TrainTypeDef
    {
        id = "mei2000", name = "名鉄2000系", capPerCar = 60, maxSpeedKmh = 120,
        accelKmhs = 2.3f, decelKmhs = 3.5f, costPerCarOku = 2.2f,
        body = Hex("2377BE"), stripe = Hex("F39800"), front = Hex("E8EEF4"),
    };
    public static readonly TrainTypeDef Mei2200 = new TrainTypeDef
    {
        id = "mei2200", name = "名鉄2200系", capPerCar = 115, maxSpeedKmh = 120,
        accelKmhs = 2.5f, decelKmhs = 3.5f, costPerCarOku = 1.7f,
        body = Hex("C9CDD2"), stripe = Hex("D5001C"), front = Hex("D5001C"),
    };
    public static readonly TrainTypeDef Mei6000 = new TrainTypeDef
    {
        id = "mei6000", name = "名鉄6000系", capPerCar = 135, maxSpeedKmh = 100,
        accelKmhs = 2.0f, decelKmhs = 3.5f, costPerCarOku = 1.2f,
        body = Hex("D5001C"), stripe = Hex("D5001C"), front = Hex("D5001C"),
    };

    public static readonly List<Formation> Formations = new List<Formation>
    {
        new Formation { type = Keio5000, cars = 10 },
        new Formation { type = Mei2000, cars = 4 },
        new Formation { type = Mei2000, cars = 8 },
        new Formation { type = Mei2200, cars = 6 },
        new Formation { type = Mei6000, cars = 2 },
        new Formation { type = Mei6000, cars = 4 },
    };
}
