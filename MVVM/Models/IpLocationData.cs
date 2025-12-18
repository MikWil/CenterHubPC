namespace CenterHubNew.MVVM.Models
{
    public class IpLocationData
    {
        public string? City { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string? Status { get; set; }
        public string? CountryCode { get; internal set; }
    }
}