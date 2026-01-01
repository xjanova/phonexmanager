using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public class DeviceImageInfo
    {
        public string Brand { get; set; } = "";
        public string Model { get; set; } = "";
        public string Codename { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public string LocalPath { get; set; } = "";
        public string Year { get; set; } = "";
    }

    public class ImageService
    {
        private readonly HttpClient _httpClient;
        private readonly string _imageCachePath;
        public event EventHandler<string>? LogMessage;

        // Brand Logo URLs (official CDN sources)
        public static readonly Dictionary<string, string> BrandLogos = new()
        {
            { "Samsung", "https://images.samsung.com/is/image/samsung/assets/global/about-us/brand/logo/mo/360_197_1.png" },
            { "Xiaomi", "https://i01.appmifile.com/webfile/globalimg/in/cms/logo.png" },
            { "Google", "https://lh3.googleusercontent.com/COxitqgJr1sJnIDe8-jiKhxDx1FrYbtRHKJ9z_hELisAlapwE9LUPh6fcXIfb5vwpbMl4xl9H9TRFPc5NOO8Sb3VSgIBrfRYvW6cUA" },
            { "OnePlus", "https://oasis.opstatics.com/content/dam/oasis/page/2021/9-series/spec-workspace/logo.png" },
            { "Huawei", "https://consumer.huawei.com/etc/designs/consumer/img/logo.png" },
            { "OPPO", "https://www.oppo.com/content/dam/oppo/common/logo/oppo-logo.png" },
            { "Vivo", "https://www.vivo.com/favicon.ico" },
            { "Realme", "https://image.realme.com/assets/images/logo.png" },
            { "Motorola", "https://motorola-global-en-uk.custhelp.com/img/enduser/logo.png" },
            { "Sony", "https://sony.scene7.com/is/image/sonyglobalsolutions/sony_logo" },
            { "LG", "https://www.lg.com/lg5-common-gp/images/common/header/logo-b2c.jpg" },
            { "Nokia", "https://www.nokia.com/sites/default/files/nokia_logo_blue_rgb.svg" },
            { "ASUS", "https://www.asus.com/media/Odin/images/logo.svg" },
            { "Lenovo", "https://www.lenovo.com/medias/lenovo-logo.png" },
            { "ZTE", "https://www.ztedevices.com/assets/images/zte-logo.png" },
            { "Honor", "https://www.hihonor.com/favicon.ico" },
            { "Nothing", "https://nothing.tech/favicon.ico" },
            { "POCO", "https://i01.appmifile.com/webfile/globalimg/in/cms/poco-logo.png" },
            { "Redmi", "https://i01.appmifile.com/webfile/globalimg/in/cms/redmi-logo.png" }
        };

        // Device image database - Latest phones 2023-2024
        public static readonly List<DeviceImageInfo> DeviceImages = new()
        {
            // Samsung 2024
            new() { Brand = "Samsung", Model = "Galaxy S24 Ultra", Codename = "e3q", Year = "2024",
                ImageUrl = "https://images.samsung.com/is/image/samsung/p6pim/th/2401/gallery/th-galaxy-s24-ultra-sm-s928-489942-sm-s928bztpthl-thumb-539521953" },
            new() { Brand = "Samsung", Model = "Galaxy S24+", Codename = "e2q", Year = "2024",
                ImageUrl = "https://images.samsung.com/is/image/samsung/p6pim/th/2401/gallery/th-galaxy-s24-plus-489570-sm-s926bzkdthl-thumb-539521835" },
            new() { Brand = "Samsung", Model = "Galaxy S24", Codename = "e1q", Year = "2024",
                ImageUrl = "https://images.samsung.com/is/image/samsung/p6pim/th/2401/gallery/th-galaxy-s24-489168-sm-s921bzyethl-thumb-539521717" },
            new() { Brand = "Samsung", Model = "Galaxy Z Fold 5", Codename = "q5q", Year = "2023",
                ImageUrl = "https://images.samsung.com/is/image/samsung/p6pim/th/2307/gallery/th-galaxy-z-fold5-f946-sm-f946bzkbthl-thumb-536853033" },
            new() { Brand = "Samsung", Model = "Galaxy Z Flip 5", Codename = "b5q", Year = "2023",
                ImageUrl = "https://images.samsung.com/is/image/samsung/p6pim/th/2307/gallery/th-galaxy-z-flip5-f731-sm-f731blbathl-thumb-536852857" },
            new() { Brand = "Samsung", Model = "Galaxy A54 5G", Codename = "a54x", Year = "2023",
                ImageUrl = "https://images.samsung.com/is/image/samsung/p6pim/th/sm-a546elbdthl/gallery/th-galaxy-a54-5g-sm-a546-sm-a546elbdthl-thumb-534826893" },
            new() { Brand = "Samsung", Model = "Galaxy A34 5G", Codename = "a34x", Year = "2023",
                ImageUrl = "https://images.samsung.com/is/image/samsung/p6pim/th/sm-a346elbdthl/gallery/th-galaxy-a34-5g-sm-a346-sm-a346elbdthl-thumb-534826761" },

            // Xiaomi/Redmi/POCO 2024
            new() { Brand = "Xiaomi", Model = "14 Ultra", Codename = "houji", Year = "2024",
                ImageUrl = "https://i01.appmifile.com/v1/MI_18455B3E4DA706226CF7535A58E875F0267/pms_1708421258.45788180.png" },
            new() { Brand = "Xiaomi", Model = "14 Pro", Codename = "shennong", Year = "2023",
                ImageUrl = "https://i01.appmifile.com/v1/MI_18455B3E4DA706226CF7535A58E875F0267/pms_1698749534.32583162.png" },
            new() { Brand = "Xiaomi", Model = "14", Codename = "houji", Year = "2023",
                ImageUrl = "https://i01.appmifile.com/v1/MI_18455B3E4DA706226CF7535A58E875F0267/pms_1698750283.82152453.png" },
            new() { Brand = "POCO", Model = "F6 Pro", Codename = "vermeer", Year = "2024",
                ImageUrl = "https://i02.appmifile.com/550_operatorx_operatorx_xm/25/02/2024/5a64dab7a1ac5d5ed8aaca2c1a72c6e3.png" },
            new() { Brand = "POCO", Model = "X6 Pro", Codename = "duchamp", Year = "2024",
                ImageUrl = "https://i02.appmifile.com/758_operatorx_operatorx_xm/08/01/2024/d47e55ae3e3e1bc7e41f9b6f4e2eacc5.png" },
            new() { Brand = "Redmi", Model = "Note 13 Pro+", Codename = "zircon", Year = "2024",
                ImageUrl = "https://i01.appmifile.com/v1/MI_18455B3E4DA706226CF7535A58E875F0267/pms_1704354682.78665171.png" },
            new() { Brand = "Redmi", Model = "Note 13 Pro", Codename = "sapphire", Year = "2024",
                ImageUrl = "https://i01.appmifile.com/v1/MI_18455B3E4DA706226CF7535A58E875F0267/pms_1704354578.47814982.png" },

            // Google Pixel 2024
            new() { Brand = "Google", Model = "Pixel 8 Pro", Codename = "husky", Year = "2023",
                ImageUrl = "https://lh3.googleusercontent.com/HenyKzEGM9r3sE7gEJGAoLqPyZPB6OXNNwDFNWTAVX4fNJQz3aK3ZpMdp5L4OjqMmOYmAJdVsXY3CG6gPX9l8d0H_Uc-a8c=rw-e365-w1000" },
            new() { Brand = "Google", Model = "Pixel 8", Codename = "shiba", Year = "2023",
                ImageUrl = "https://lh3.googleusercontent.com/2VM7R5G-dn8T_WJKGpPHVQO8O5O3L6HXPO5vN7MKoYY3aB4cq9e_X9iXw=rw-e365-w1000" },
            new() { Brand = "Google", Model = "Pixel 8a", Codename = "akita", Year = "2024",
                ImageUrl = "https://lh3.googleusercontent.com/qADPXSFjnKq9OPcQQW7Dz4Wc9VkN4MTU1HsWJvIvHD8WSJWN3dKZMf4=rw-e365-w1000" },
            new() { Brand = "Google", Model = "Pixel Fold", Codename = "felix", Year = "2023",
                ImageUrl = "https://lh3.googleusercontent.com/A_xRPDPK5u6Qw1p8lT8_4PWIj8q2d0eG_9F4Nk5dL1pMsq_LmF2W=rw-e365-w1000" },

            // OnePlus 2024
            new() { Brand = "OnePlus", Model = "12", Codename = "waffle", Year = "2024",
                ImageUrl = "https://oasis.opstatics.com/content/dam/oasis/page/2024/na/oneplus-12/specs/silky-black.png" },
            new() { Brand = "OnePlus", Model = "12R", Codename = "aston", Year = "2024",
                ImageUrl = "https://oasis.opstatics.com/content/dam/oasis/page/2024/na/oneplus-12r/specs/iron-gray.png" },
            new() { Brand = "OnePlus", Model = "Open", Codename = "aston", Year = "2023",
                ImageUrl = "https://oasis.opstatics.com/content/dam/oasis/page/2023/na/oneplus-open/specs/voyager-black.png" },
            new() { Brand = "OnePlus", Model = "Nord CE 4", Codename = "larry", Year = "2024",
                ImageUrl = "https://oasis.opstatics.com/content/dam/oasis/page/2024/eu/nord-ce-4/specs/dark-chrome.png" },

            // Nothing 2024
            new() { Brand = "Nothing", Model = "Phone (2)", Codename = "pong", Year = "2023",
                ImageUrl = "https://nothing.tech/cdn/shop/products/nothing-phone-2-white.png" },
            new() { Brand = "Nothing", Model = "Phone (2a)", Codename = "pacman", Year = "2024",
                ImageUrl = "https://nothing.tech/cdn/shop/products/nothing-phone-2a-black.png" },

            // OPPO 2024
            new() { Brand = "OPPO", Model = "Find X7 Ultra", Codename = "OP5D41L1", Year = "2024",
                ImageUrl = "https://image.oppo.com/content/dam/oppo/product-asset-library/find/find-x7-ultra/v1/assets/images/find-x7-ultra-ocean-blue.png" },
            new() { Brand = "OPPO", Model = "Reno 11 Pro", Codename = "OP5D51L1", Year = "2024",
                ImageUrl = "https://image.oppo.com/content/dam/oppo/product-asset-library/reno/reno11-pro/v1/assets/images/reno11-pro-pearl-white.png" },

            // Realme 2024
            new() { Brand = "Realme", Model = "GT 5 Pro", Codename = "RE58D0L1", Year = "2023",
                ImageUrl = "https://image.realme.com/assets/images/products/realme-gt5-pro/images/gt5-pro-black.png" },
            new() { Brand = "Realme", Model = "12 Pro+", Codename = "RE58D1L1", Year = "2024",
                ImageUrl = "https://image.realme.com/assets/images/products/realme-12-pro-plus/images/12-pro-plus-blue.png" },

            // Vivo 2024
            new() { Brand = "Vivo", Model = "X100 Pro", Codename = "V2324A", Year = "2023",
                ImageUrl = "https://www.vivo.com/content/dam/vivo/products/x100-pro/images/x100-pro-blue.png" },
            new() { Brand = "Vivo", Model = "X100 Ultra", Codename = "V2325A", Year = "2024",
                ImageUrl = "https://www.vivo.com/content/dam/vivo/products/x100-ultra/images/x100-ultra-white.png" },

            // Honor 2024
            new() { Brand = "Honor", Model = "Magic 6 Pro", Codename = "BVL-AN10", Year = "2024",
                ImageUrl = "https://www.hihonor.com/content/dam/honor/in/product/magic6-pro/magic6-pro-black.png" },
            new() { Brand = "Honor", Model = "Magic V2", Codename = "VER-AN10", Year = "2023",
                ImageUrl = "https://www.hihonor.com/content/dam/honor/in/product/magic-v2/magic-v2-black.png" },

            // Motorola 2024
            new() { Brand = "Motorola", Model = "Edge 40 Pro", Codename = "rtwo", Year = "2023",
                ImageUrl = "https://motorolain.vtexassets.com/arquivos/edge-40-pro-black.png" },
            new() { Brand = "Motorola", Model = "Razr 40 Ultra", Codename = "zeekr", Year = "2023",
                ImageUrl = "https://motorolain.vtexassets.com/arquivos/razr-40-ultra-black.png" },

            // Sony 2024
            new() { Brand = "Sony", Model = "Xperia 1 V", Codename = "pdx234", Year = "2023",
                ImageUrl = "https://sony.scene7.com/is/image/sonyglobalsolutions/Primary_image-6" },
            new() { Brand = "Sony", Model = "Xperia 5 V", Codename = "pdx237", Year = "2023",
                ImageUrl = "https://sony.scene7.com/is/image/sonyglobalsolutions/Primary_image-7" },

            // ASUS ROG 2024
            new() { Brand = "ASUS", Model = "ROG Phone 8 Pro", Codename = "AI2401", Year = "2024",
                ImageUrl = "https://dlcdnwebimgs.asus.com/files/media/54C25E9E-D70E-4D23-9855-A96B8F3C0F86/v1/img/phantom-black.png" },
            new() { Brand = "ASUS", Model = "Zenfone 11 Ultra", Codename = "AI2401_D", Year = "2024",
                ImageUrl = "https://dlcdnwebimgs.asus.com/files/media/zenfone-11-ultra-black.png" }
        };

        public ImageService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PhoneRomFlashTool/1.0");

            _imageCachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhoneRomFlashTool", "ImageCache");

            if (!Directory.Exists(_imageCachePath))
            {
                Directory.CreateDirectory(_imageCachePath);
            }
        }

        public async Task<string> DownloadBrandLogoAsync(string brand)
        {
            if (!BrandLogos.TryGetValue(brand, out var url))
            {
                return "";
            }

            var localPath = Path.Combine(_imageCachePath, "brands", $"{brand.ToLower()}.png");

            if (File.Exists(localPath))
            {
                return localPath;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                var imageData = await _httpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(localPath, imageData);
                LogMessage?.Invoke(this, $"Downloaded logo for {brand}");
                return localPath;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Failed to download {brand} logo: {ex.Message}");
                return "";
            }
        }

        public async Task<string> DownloadDeviceImageAsync(string brand, string model)
        {
            var device = DeviceImages.Find(d =>
                d.Brand.Equals(brand, StringComparison.OrdinalIgnoreCase) &&
                d.Model.Equals(model, StringComparison.OrdinalIgnoreCase));

            if (device == null || string.IsNullOrEmpty(device.ImageUrl))
            {
                return "";
            }

            var safeFileName = $"{brand}_{model}".Replace(" ", "_").Replace("/", "_");
            var localPath = Path.Combine(_imageCachePath, "devices", $"{safeFileName}.png");

            if (File.Exists(localPath))
            {
                return localPath;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                var imageData = await _httpClient.GetByteArrayAsync(device.ImageUrl);
                await File.WriteAllBytesAsync(localPath, imageData);
                LogMessage?.Invoke(this, $"Downloaded image for {brand} {model}");
                return localPath;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Failed to download {brand} {model} image: {ex.Message}");
                return "";
            }
        }

        public async Task DownloadAllBrandLogosAsync()
        {
            foreach (var brand in BrandLogos.Keys)
            {
                await DownloadBrandLogoAsync(brand);
                await Task.Delay(100); // Rate limiting
            }
        }

        public async Task DownloadAllDeviceImagesAsync(IProgress<int>? progress = null)
        {
            int total = DeviceImages.Count;
            int current = 0;

            foreach (var device in DeviceImages)
            {
                await DownloadDeviceImageAsync(device.Brand, device.Model);
                current++;
                progress?.Report((current * 100) / total);
                await Task.Delay(100); // Rate limiting
            }
        }

        public string GetCachedBrandLogo(string brand)
        {
            var path = Path.Combine(_imageCachePath, "brands", $"{brand.ToLower()}.png");
            return File.Exists(path) ? path : "";
        }

        public string GetCachedDeviceImage(string brand, string model)
        {
            var safeFileName = $"{brand}_{model}".Replace(" ", "_").Replace("/", "_");
            var path = Path.Combine(_imageCachePath, "devices", $"{safeFileName}.png");
            return File.Exists(path) ? path : "";
        }

        public List<DeviceImageInfo> GetDevicesByBrand(string brand)
        {
            return DeviceImages.FindAll(d => d.Brand.Equals(brand, StringComparison.OrdinalIgnoreCase));
        }

        public List<DeviceImageInfo> GetDevicesByYear(string year)
        {
            return DeviceImages.FindAll(d => d.Year == year);
        }

        public List<string> GetAvailableBrands()
        {
            var brands = new HashSet<string>();
            foreach (var device in DeviceImages)
            {
                brands.Add(device.Brand);
            }
            return new List<string>(brands);
        }
    }
}
