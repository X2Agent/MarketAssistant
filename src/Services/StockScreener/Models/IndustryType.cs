using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace MarketAssistant.Services.StockScreener.Models;

/// <summary>
/// 行业分类枚举（精确匹配雪球网行业分类）
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IndustryType>))]
public enum IndustryType
{
    /// <summary>
    /// 全部行业（不限制行业）- 默认值，当用户未指定任何具体行业时使用
    /// </summary>
    [Description("全部 - 不限制行业，用户未提到任何具体行业时必须使用此选项")]
    [EnumMember(Value = "全部")]
    All = 0,

    // 科技类
    /// <summary>
    /// 计算机设备
    /// </summary>
    [Description("计算机设备")]
    [EnumMember(Value = "计算机设备")]
    ComputerEquipment,

    /// <summary>
    /// 软件开发
    /// </summary>
    [Description("软件开发")]
    [EnumMember(Value = "软件开发")]
    SoftwareDevelopment,

    /// <summary>
    /// 半导体
    /// </summary>
    [Description("半导体")]
    [EnumMember(Value = "半导体")]
    Semiconductor,

    // 新能源类
    /// <summary>
    /// 电池
    /// </summary>
    [Description("电池")]
    [EnumMember(Value = "电池")]
    Battery,

    /// <summary>
    /// 光伏设备
    /// </summary>
    [Description("光伏设备")]
    [EnumMember(Value = "光伏设备")]
    PhotovoltaicEquipment,

    /// <summary>
    /// 风电设备
    /// </summary>
    [Description("风电设备")]
    [EnumMember(Value = "风电设备")]
    WindPowerEquipment,

    // 医药类
    /// <summary>
    /// 化学制药
    /// </summary>
    [Description("化学制药")]
    [EnumMember(Value = "化学制药")]
    ChemicalPharmaceutical,

    /// <summary>
    /// 生物制品
    /// </summary>
    [Description("生物制品")]
    [EnumMember(Value = "生物制品")]
    BiologicalProducts,

    /// <summary>
    /// 医疗器械
    /// </summary>
    [Description("医疗器械")]
    [EnumMember(Value = "医疗器械")]
    MedicalDevices,

    // 消费类
    /// <summary>
    /// 白酒
    /// </summary>
    [Description("白酒")]
    [EnumMember(Value = "白酒")]
    Liquor,

    /// <summary>
    /// 饮料乳品
    /// </summary>
    [Description("饮料乳品")]
    [EnumMember(Value = "饮料乳品")]
    BeveragesDairy,

    /// <summary>
    /// 食品加工
    /// </summary>
    [Description("食品加工")]
    [EnumMember(Value = "食品加工")]
    FoodProcessing,

    // 金融类
    /// <summary>
    /// 股份制银行
    /// </summary>
    [Description("股份制银行")]
    [EnumMember(Value = "股份制银行")]
    JointStockBank,

    /// <summary>
    /// 国有大型银行
    /// </summary>
    [Description("国有大型银行")]
    [EnumMember(Value = "国有大型银行")]
    StateBanks,

    // 房地产
    /// <summary>
    /// 房地产开发
    /// </summary>
    [Description("房地产开发")]
    [EnumMember(Value = "房地产开发")]
    RealEstateDevelopment,

    // 汽车类
    /// <summary>
    /// 乘用车
    /// </summary>
    [Description("乘用车")]
    [EnumMember(Value = "乘用车")]
    PassengerVehicles,

    /// <summary>
    /// 汽车零部件
    /// </summary>
    [Description("汽车零部件")]
    [EnumMember(Value = "汽车零部件")]
    AutoParts,

    // 通信类
    /// <summary>
    /// 通信设备
    /// </summary>
    [Description("通信设备")]
    [EnumMember(Value = "通信设备")]
    CommunicationEquipment,

    /// <summary>
    /// 通信服务
    /// </summary>
    [Description("通信服务")]
    [EnumMember(Value = "通信服务")]
    CommunicationServices,

    // 电力
    /// <summary>
    /// 电力
    /// </summary>
    [Description("电力")]
    [EnumMember(Value = "电力")]
    Power,

    // 化工类
    /// <summary>
    /// 化学原料
    /// </summary>
    [Description("化学原料")]
    [EnumMember(Value = "化学原料")]
    ChemicalMaterials,

    /// <summary>
    /// 化学制品
    /// </summary>
    [Description("化学制品")]
    [EnumMember(Value = "化学制品")]
    ChemicalProducts,

    // 机械类
    /// <summary>
    /// 工程机械
    /// </summary>
    [Description("工程机械")]
    [EnumMember(Value = "工程机械")]
    ConstructionMachinery,

    /// <summary>
    /// 专用设备
    /// </summary>
    [Description("专用设备")]
    [EnumMember(Value = "专用设备")]
    SpecializedEquipment,

    // 家电类
    /// <summary>
    /// 白色家电
    /// </summary>
    [Description("白色家电")]
    [EnumMember(Value = "白色家电")]
    WhiteAppliances,

    /// <summary>
    /// 小家电
    /// </summary>
    [Description("小家电")]
    [EnumMember(Value = "小家电")]
    SmallAppliances
}
