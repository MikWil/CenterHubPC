using System.Collections.Generic;

namespace CenterHubNew.MVVM.Models
{
    public class Country
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<City> Cities { get; set; } = new();
    }

    public class City
    {
        public string Name { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    public static class LocationData
    {
        public static List<Country> Countries { get; } = new()
        {
            new Country
            {
                Code = "SE",
                Name = "Sweden",
                Cities = new List<City>
                {
                    new() { Name = "Stockholm", Latitude = 59.3293, Longitude = 18.0686 },
                    new() { Name = "Gothenburg", Latitude = 57.7089, Longitude = 11.9746 },
                    new() { Name = "Malmö", Latitude = 55.6059, Longitude = 13.0007 },
                    new() { Name = "Uppsala", Latitude = 59.8586, Longitude = 17.6389 },
                    new() { Name = "Västerås", Latitude = 59.6099, Longitude = 16.5448 }
                }
            },
            new Country
            {
                Code = "NO",
                Name = "Norway",
                Cities = new List<City>
                {
                    new() { Name = "Oslo", Latitude = 59.9139, Longitude = 10.7522 },
                    new() { Name = "Bergen", Latitude = 60.3913, Longitude = 5.3221 },
                    new() { Name = "Trondheim", Latitude = 63.4305, Longitude = 10.3951 },
                    new() { Name = "Stavanger", Latitude = 58.9700, Longitude = 5.7331 }
                }
            },
            new Country
            {
                Code = "DK",
                Name = "Denmark",
                Cities = new List<City>
                {
                    new() { Name = "Copenhagen", Latitude = 55.6761, Longitude = 12.5683 },
                    new() { Name = "Aarhus", Latitude = 56.1629, Longitude = 10.2039 },
                    new() { Name = "Odense", Latitude = 55.4038, Longitude = 10.4024 },
                    new() { Name = "Aalborg", Latitude = 57.0488, Longitude = 9.9217 }
                }
            }
        };
    }
} 