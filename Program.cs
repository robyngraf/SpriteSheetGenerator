using ImageMagick;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;

string spritesPath = @"F:\Metagame\dev\sprites";
string outputPath = @"F:\Metagame\dev\spritesheet.gif";
string outputPathB64 = @"F:\Metagame\dev\spritesheet_b64.txt";
var metadataPath = @"F:\Metagame\dev\spritesheet_metadata.txt";

List<SpriteData> sprites = [];
foreach (var filePath in Directory.EnumerateFiles(spritesPath, "*.*", SearchOption.AllDirectories))
{
    Bitmap bitmap;
    try
    {
        bitmap = new Bitmap(filePath);
    }
    catch (Exception)
    {
        using var magickImage = new MagickImage(filePath);
        using var ms = new MemoryStream();
        magickImage.Write(ms, MagickFormat.Bmp);
        bitmap = new Bitmap(ms);
    }
    var name = Path.GetFileNameWithoutExtension(filePath);
    name = name.Replace(" bw", "", StringComparison.InvariantCultureIgnoreCase);
    name = name.Replace(" anim", "", StringComparison.InvariantCultureIgnoreCase);
    sprites.Add(new SpriteData { Bitmap = bitmap, Name = name });
}
sprites = [.. sprites.OrderByDescending(s => s.Height).ThenByDescending(s => s.Width)];

// TODO: algorithm can be improved - could try to fit small sprites in gaps between
// sprite.Height and maxHeightInRow, starting with the widest
// - but no point until I have more sprites to test it with

var spriteSheetSize = 64;
// Algorithm: we determine the size of the sprite sheet by iteratively trying to
// fit the bitmaps into a square of size 'spriteSheetSize' x 'spriteSheetSize'. If we can't fit all the bitmaps,
// we increase the size of the sprite sheet and try again.
// We fit as many sprites as we can into each row, then move on to the next row with
// the remaining sprites.
// The sprites are sorted by height, to get mostly sprites of the same size in each row.

var ableToFit = true;
do
{
    ableToFit = true;
    spriteSheetSize *= 2;
    foreach (var sprite in sprites)
    {
        if (sprite.Width > spriteSheetSize || sprite.Height > spriteSheetSize)
        {
            ableToFit = false;
            break;
        }
    }
    if (!ableToFit) continue;

    var currentX = 0;
    var currentY = 0;
    ableToFit = true;
    List<SpriteData> remainingSprites = [.. sprites];
    while (remainingSprites.Count > 0)
    {
        var maxHeightInRow = 0;
        for (int i = 0; i < remainingSprites.Count; i++)
        {
            var sprite = remainingSprites[i];
            if (currentX + sprite.Width > spriteSheetSize) continue;

            sprite.Position = new Point(currentX, currentY);
            currentX += sprite.Width;
            maxHeightInRow = Math.Max(maxHeightInRow, sprite.Height);
            remainingSprites.RemoveAt(i);
            i--;
        }
        currentX = 0;
        currentY += maxHeightInRow;
        if (currentY > spriteSheetSize)
        {
            ableToFit = false;
            break;
        }
    }
} while (!ableToFit);

// After that, we create a new bitmap of the determined size and draw all the bitmaps onto it using the data generated in the previous stage.

var spriteSheet = new Bitmap(spriteSheetSize, spriteSheetSize);
using (var graphics = Graphics.FromImage(spriteSheet))
{
    foreach (var sprite in sprites)
    {
        Console.WriteLine($"{sprite.Name}: {sprite.Size} @ {sprite.Position}");
        if (sprite.Name == "terrain")
        {
            //foreach (var prop in sprite.Bitmap.PropertyItems) { Console.WriteLine($"{prop.Id:X} {Encoding.UTF8.GetString(prop.Value)}"); }
            graphics.DrawImage(sprite.Bitmap, sprite.X, sprite.Y + sprite.Height, sprite.Width, sprite.Height);
        }
        else
        {
            // Specify the width and height here to prevent automatic scaling of the sprites
            graphics.DrawImage(sprite.Bitmap, sprite.X, sprite.Y, sprite.Width, sprite.Height);
        }
    }
}

// 1. Convert GDI+ Bitmap to MemoryStream
using MemoryStream ms2 = new();
spriteSheet.Save(ms2, ImageFormat.Png);
ms2.Position = 0;

// 2. Read into ImageMagick
using MagickImage image = new(ms2);
image.Format = MagickFormat.Gif;
image.Write(outputPath);
var bytes = File.ReadAllBytes(outputPath);
var fileString = "data:image/gif;base64," + Convert.ToBase64String(bytes).TrimEnd('=');
File.WriteAllText(outputPathB64, fileString, Encoding.UTF8);

// Save the metadata of the sprites in a text file in JSON format, which can be used later to extract the individual sprites from the sprite sheet.
var metadata = sprites.ToDictionary(s => s.Name, s => new
{
    x = s.Position.X,
    y = s.Position.Y,
    w = s.Width,
    h = s.Height
});

var json = JsonSerializer.Serialize(metadata);
File.WriteAllText(metadataPath, json, Encoding.UTF8);
Console.WriteLine($"Wrote metadata for {sprites.Count} sprites to {metadataPath}");
