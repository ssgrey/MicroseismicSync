using System;

namespace MicroseismicSync.Models
{
    public enum StyleCategoryType
    {
        TetSuiteSettings = 1000,
        Llm = 2000,
        TET_SUITE_SETTINGS = TetSuiteSettings,
        LLM = Llm,
    }

    public enum StyleSubCategoryType
    {
        WellLogTemplate = 1001,
        HomePage2DPlan = 1002,
        WellDataColumns = 1003,
        WellLogDepthRangeTemplate = 1004,
        GrdProdPredictionColumns = 1005,
        SurgeParamsStore = 1006,
        ChartStyleConfig = 1007,
        AgentDeckData = 2001,
        WELL_LOG_TEMPLATE = WellLogTemplate,
        HOME_PAGE_2D_PLAN = HomePage2DPlan,
        WELL_DATA_COLUMNS = WellDataColumns,
        WELLLOG_DEPTH_RANGE_TEMPLATE = WellLogDepthRangeTemplate,
        GRD_PROD_PREDICTION_COLUMNS = GrdProdPredictionColumns,
        SURGE_PARAMS_STORE = SurgeParamsStore,
        CHART_STYLE_CONFIG = ChartStyleConfig,
        AGENT_DECK_DATA = AgentDeckData,
    }

    public enum StyleFileScope
    {
        Global = 0,
        Project = 1,
        Private = 2,
    }

    public sealed class GetStyleFileListRequest
    {
        public string Name { get; set; }

        public StyleCategoryType Category { get; set; } = StyleCategoryType.Llm;

        public StyleSubCategoryType Subcategory { get; set; } = StyleSubCategoryType.AgentDeckData;

        public int? Version { get; set; }
    }

    public sealed class StyleFileInfo
    {
        public string Id { get; set; }

        public StyleCategoryType Category { get; set; }

        public StyleSubCategoryType Subcategory { get; set; }

        public string Name { get; set; }

        public StyleFileScope Scope { get; set; }

        public int Version { get; set; }

        public DateTime CreateTime { get; set; }

        public DateTime UpdateTime { get; set; }
    }

    public sealed class CreateStyleFileRequest
    {
        public StyleCategoryType Category { get; set; } = StyleCategoryType.Llm;

        public StyleSubCategoryType Subcategory { get; set; } = StyleSubCategoryType.AgentDeckData;

        public string Name { get; set; }

        public StyleFileScope Scope { get; set; } = StyleFileScope.Project;

        public string FilePath { get; set; }

        public string FileName { get; set; }

        public bool ForceOverwrite { get; set; } = true;
    }
}
