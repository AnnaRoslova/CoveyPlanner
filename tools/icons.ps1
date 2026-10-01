# Иконки приложения из одного исходника — квадратной картинки 1024×1024 с фоном до краёв (docs/icon-1024.png).
# Запуск из корня репозитория (в PowerShell):
#   powershell -File tools/icons.ps1
#   powershell -File tools/icons.ps1 -Source путь\к\картинке.png
# Создаёт в src/Planner.Client/wwwroot:
#   icon-192.png, icon-512.png                 — обычные иконки PWA (картинка целиком)
#   icon-maskable-192.png, icon-maskable-512.png — для масок (круг, «капля» и т. п.): картинка уменьшена до 80%,
#                                                 чтобы всё важное было в безопасной зоне; поля — размытый фон
#                                                 той же картинки, края мягкие, без шва
#   apple-touch-icon.png (180)                 — «На экран Домой» на iPhone (углы скругляет сама iOS)
#   favicon.ico (16, 32, 48)                   — вкладка браузера
param(
    [string]$Source = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Source) { $Source = Join-Path $root 'docs/icon-1024.png' }
$out = Join-Path $root 'src/Planner.Client/wwwroot'

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class PlannerIcons
{
    // Уменьшение в высоком качестве; края не темнеют (TileFlipXY)
    public static Bitmap Resize(Image source, int size)
    {
        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        using (var attributes = new ImageAttributes())
        {
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.DrawImage(source, new Rectangle(0, 0, size, size), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }
        return result;
    }

    // Иконка для масок: картинка на scale от размера с мягкими краями; поля — продолжение её краёв
    public static Bitmap Maskable(Image source, int size, double scale)
    {
        int inner = (int)Math.Round(size * scale);
        int offset = (size - inner) / 2;
        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var picture = Resize(source, inner))
        {
            // Поля: цвет ближайшей точки края размытой картинки — на стыке цвета совпадают, шва нет
            using (var tiny = Resize(picture, 64))
            using (var smooth = Resize(tiny, inner))
            {
                var from = smooth.LockBits(new Rectangle(0, 0, inner, inner), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var to = result.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                var pixel = new int[1];
                for (int y = 0; y < size; y++)
                {
                    int sy = Math.Max(0, Math.Min(inner - 1, y - offset));
                    for (int x = 0; x < size; x++)
                    {
                        int sx = Math.Max(0, Math.Min(inner - 1, x - offset));
                        System.Runtime.InteropServices.Marshal.Copy(from.Scan0 + sy * from.Stride + sx * 4, pixel, 0, 1);
                        System.Runtime.InteropServices.Marshal.Copy(pixel, 0, to.Scan0 + y * to.Stride + x * 4, 1);
                    }
                }
                smooth.UnlockBits(from);
                result.UnlockBits(to);
            }

            // Мягкий край: прозрачность нарастает к краю на узкой полосе (2,5% стороны) — не задевает уголки листа
            double band = inner * 0.025;
            var data = picture.LockBits(new Rectangle(0, 0, inner, inner), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            var row = new byte[data.Stride];
            for (int y = 0; y < inner; y++)
            {
                IntPtr line = data.Scan0 + y * data.Stride;
                System.Runtime.InteropServices.Marshal.Copy(line, row, 0, data.Stride);
                for (int x = 0; x < inner; x++)
                {
                    double edge = Math.Min(Math.Min(x + 0.5, inner - x - 0.5), Math.Min(y + 0.5, inner - y - 0.5));
                    double alpha = Math.Min(1.0, edge / band);
                    alpha = alpha * alpha * (3 - 2 * alpha); // плавная кривая
                    row[x * 4 + 3] = (byte)Math.Round(row[x * 4 + 3] * alpha);
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, line, data.Stride);
            }
            picture.UnlockBits(data);

            using (var g = Graphics.FromImage(result))
            {
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(picture, offset, offset, inner, inner);
            }
        }
        return result;
    }

    // Непрозрачная картинка (для iOS и обычных иконок): без альфа-канала
    public static void SaveOpaquePng(Bitmap bitmap, string path)
    {
        using (var opaque = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb))
        {
            using (var g = Graphics.FromImage(opaque))
            {
                g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);
            }
            opaque.Save(path, ImageFormat.Png);
        }
    }

    // favicon.ico: несколько размеров, каждый — PNG внутри ICO (понимают все современные браузеры)
    public static void SaveIco(Image source, int[] sizes, string path)
    {
        var images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            using (var bitmap = Resize(source, sizes[i]))
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                images[i] = stream.ToArray();
            }
        }

        using (var file = new BinaryWriter(File.Create(path)))
        {
            file.Write((short)0);            // зарезервировано
            file.Write((short)1);            // тип: значок
            file.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                file.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                file.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                file.Write((byte)0);         // палитра не используется
                file.Write((byte)0);
                file.Write((short)1);        // плоскости
                file.Write((short)32);       // бит на пиксель
                file.Write(images[i].Length);
                file.Write(offset);
                offset += images[i].Length;
            }
            foreach (var image in images)
            {
                file.Write(image);
            }
        }
    }
}
'@

$image = [System.Drawing.Image]::FromFile((Resolve-Path $Source))
try {
    if ($image.Width -ne $image.Height) { throw "Картинка должна быть квадратной: $($image.Width)×$($image.Height)" }

    foreach ($size in 192, 512) {
        $icon = [PlannerIcons]::Resize($image, $size)
        [PlannerIcons]::SaveOpaquePng($icon, (Join-Path $out "icon-$size.png")); $icon.Dispose()
        $icon = [PlannerIcons]::Maskable($image, $size, 0.8)
        [PlannerIcons]::SaveOpaquePng($icon, (Join-Path $out "icon-maskable-$size.png")); $icon.Dispose()
    }
    $icon = [PlannerIcons]::Resize($image, 180)
    [PlannerIcons]::SaveOpaquePng($icon, (Join-Path $out 'apple-touch-icon.png')); $icon.Dispose()
    [PlannerIcons]::SaveIco($image, @(16, 32, 48), (Join-Path $out 'favicon.ico'))
}
finally {
    $image.Dispose()
}

Write-Host "Иконки — в $out" -ForegroundColor Green
