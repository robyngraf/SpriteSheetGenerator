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
    name = name.Replace("_bw", "", StringComparison.InvariantCultureIgnoreCase);
    name = name.Replace(" bw", "", StringComparison.InvariantCultureIgnoreCase);
    name = name.Replace(" anim", "", StringComparison.InvariantCultureIgnoreCase);
    sprites.Add(new SpriteData { Bitmap = bitmap, Name = name });
}
sprites = [.. sprites.OrderByDescending(s => s.Height).ThenByDescending(s => s.Width).ThenBy(s => s.Name)];
var smallestSpriteHeight = sprites.Min(s => s.Height);
var secondSmallestSpriteHeight = sprites.Where(s => s.Height > smallestSpriteHeight).Min(s => s.Height);
var spriteSheetSize = 64;
// Algorithm: we determine the size of the sprite sheet by iteratively trying to
// fit the bitmaps into a square of size 'spriteSheetSize' x 'spriteSheetSize'.
// If we can't fit all the bitmaps,
// we increase the size of the sprite sheet and try again.

// We iteratively split up the area into three when adding each sprite
// (the sprite, the row to its right, and the area below that row),
// but also iteratively merge adjacent empty areas when possible.

// The sprites are sorted by height and areas are sorted by Y position,
// to get mostly sprites of the same size in each row & also to not fill a row with
// all the small sprites before we know whether there are gaps around the
// big sprites they could fit into.

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

    List<SpriteData> remainingSprites = [.. sprites];
    List<Rectangle> areasToFill = [new Rectangle(0,0,spriteSheetSize, spriteSheetSize)];

    void AddOrMergeRect(int x, int y, int width, int height)
    {
        var rect = new Rectangle(x, y, width, height);
        foreach (var a in areasToFill.OrderBy(a => a.Y).ThenBy(a => a.X))
        {
            Rectangle? newRect = null;
            if (a.Y == rect.Y && a.Height == rect.Height)
            {
                if (a.X == rect.X + rect.Width)
                {
                    // Merge with area on the left
                    newRect = new Rectangle(rect.X, a.Y, a.Width + rect.Width, a.Height);
                }
                else if (a.X + a.Width == rect.X)
                {
                    // Merge with area on the right
                    newRect = new Rectangle(a.X, a.Y, a.Width + rect.Width, a.Height);
                }
            }
            else if (a.X == rect.X && a.Width == rect.Width)
            {
                if (a.Y == rect.Y + rect.Height)
                {
                    // Merge with area above
                    newRect = new Rectangle(a.X, rect.Y, a.Width, a.Height + rect.Height);
                }
                else if (a.Y + a.Height == rect.Y)
                {
                    // Merge with area below
                    newRect = new Rectangle(a.X, a.Y, a.Width, a.Height + rect.Height);
                }
            }
            if (newRect is not null)
            {
                areasToFill.Remove(a);
                // try to merge the newly merged rect also
                var r = newRect.Value;
                AddOrMergeRect(r.X, r.Y, r.Width, r.Height);
                return;
            }
        }
        areasToFill.Add(rect);
    }

    void FitInRect(Rectangle r)
    {
        var (x, y, width, height) = (r.X, r.Y, r.Width, r.Height);
        var sprite = remainingSprites.FirstOrDefault(s => s.Width <= width && s.Height <= height);
        if (sprite is null) return;
        remainingSprites.Remove(sprite);
        sprite.Position = new Point(x, y);
        // Fill the remaining area on the right
        AddOrMergeRect(x + sprite.Width, y, width - sprite.Width, sprite.Height);
        // Fill the remaining area below
        AddOrMergeRect(x, y + sprite.Height, width, height - sprite.Height);
    }

    // Try to fit sprites in the sprite sheet
    while (areasToFill.Count > 0 && remainingSprites.Count > 0)
    {
        IEnumerable<Rectangle> selection = areasToFill;
        if (remainingSprites.Any(s => s.Height > secondSmallestSpriteHeight))
        {
            selection = selection.Where(a => a.Height > secondSmallestSpriteHeight);
        }
        else if (remainingSprites.Any(s => s.Height > smallestSpriteHeight))
        {
            selection = selection.Where(a => a.Height > smallestSpriteHeight);
        }
        var area = selection.OrderBy(a => a.Y).ThenBy(a => a.X).First();
        areasToFill.Remove(area);
        FitInRect(area);
    }

    ableToFit = remainingSprites.Count == 0;
} while (!ableToFit);

// After that, we create a new bitmap of the determined size and draw all the bitmaps onto it using the data generated in the previous stage.

var spriteSheet = new Bitmap(spriteSheetSize, spriteSheetSize);
using (var graphics = Graphics.FromImage(spriteSheet))
{
    foreach (var sprite in sprites)
    {
        Console.WriteLine($"{sprite.Name}: {sprite.Size} @ {sprite.Position}");
        // Specify the width and height here to prevent automatic scaling/flipping/whatever of the sprites
        graphics.DrawImage(sprite.Bitmap, sprite.X, sprite.Y, sprite.Width, sprite.Height);
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
