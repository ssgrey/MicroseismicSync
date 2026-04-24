using MicroseismicSync.Infrastructure;

namespace MicroseismicSync.Models
{
    public sealed class WellInfo : ObservableObject
    {
        private bool isChecked = true;

        public bool IsChecked
        {
            get { return isChecked; }
            set { SetProperty(ref isChecked, value); }
        }

        public int BoreholeId { get; set; }

        public string BoreholeName { get; set; }

        public int WellId { get; set; }

        public string WellName { get; set; }

        public string WellNumber { get; set; }

        public string Uwi { get; set; }

        public double? SurfaceX { get; set; }

        public double? SurfaceY { get; set; }

        public double? BottomX { get; set; }

        public double? BottomY { get; set; }

        public double? Kb { get; set; }

        public string Country { get; set; }

        public string Region { get; set; }

        public string Districts { get; set; }

        public string State { get; set; }

        public string County { get; set; }

        public double? Longitude { get; set; }

        public double? Latitude { get; set; }

        public string StateDisplay
        {
            get { return string.IsNullOrWhiteSpace(State) ? Region : State; }
        }

        public string CountyDisplay
        {
            get { return string.IsNullOrWhiteSpace(County) ? Districts : County; }
        }
    }
}
