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
var largestDimension = sprites.Max(s => Math.Max(s.Width, s.Height));
var spriteSheetSize = (int)Math.Pow(2, Math.Ceiling(Math.Log(largestDimension, 2)));
// Algorithm: we determine the size of the sprite sheet by iteratively trying to
// fit the bitmaps into a square of size 'spriteSheetSize' x 'spriteSheetSize'.
// If we can't fit all the bitmaps,
// we increase the size of the sprite sheet and try again.

// We iteratively split up the area into three when adding each sprite
// (the sprite, the row to its right, and the area below that row),
// but also iteratively merge adjacent empty areas when possible.

// The tallest sprites are placed first and areas are selected by size and Y position,
// to get mostly sprites of the same size in each row & also to not fill a row with
// all the small sprites before we have merged any gaps around the
// big sprites they could fit into.

bool ableToFit;
do
{
    Queue<SpriteData> remainingSprites = new(sprites);
    HashSet<Rectangle> remainingAreas = [new Rectangle(0,0,spriteSheetSize, spriteSheetSize)];

    void AddOrMergeRect(Rectangle rect)
    {
        foreach (var a in remainingAreas.OrderBy(a => a.Y).ThenBy(a => a.X))
        {
            Rectangle? newRect = null;
            if (a.X == rect.X && a.Width == rect.Width)
            {
                if (a.Y == rect.Y + rect.Height) // Merge with area above
                    newRect = new Rectangle(a.X, rect.Y, a.Width, a.Height + rect.Height);
                else if (a.Y + a.Height == rect.Y) // Merge with area below
                    newRect = new Rectangle(a.X, a.Y, a.Width, a.Height + rect.Height);
            }
            else if (a.Y == rect.Y && a.Height == rect.Height)
            {
                if (a.X == rect.X + rect.Width) // Merge with area on the left
                    newRect = new Rectangle(rect.X, a.Y, a.Width + rect.Width, a.Height);
                else if (a.X + a.Width == rect.X) // Merge with area on the right
                    newRect = new Rectangle(a.X, a.Y, a.Width + rect.Width, a.Height);
            }
            if (newRect is not null)
            { // Iteratively try to merge the newly merged rect also
                remainingAreas.Remove(a);
                AddOrMergeRect(newRect.Value);
                return;
            }
        }
        remainingAreas.Add(rect);
    }
    // Try to fit sprites in the sprite sheet
    while (remainingAreas.Count > 0 && remainingSprites.Count > 0)
    {
        // Largest sprite
        var sprite = remainingSprites.Dequeue();

        // Select top-leftmost area the sprite fits into
        var selection = remainingAreas
            .Where(a => a.Height >= sprite.Height && a.Width >= sprite.Width)
            .ToList();
        if (selection.Count == 0) break; // No area big enough, give up
        var area = selection.OrderBy(a => a.Y).ThenBy(a => a.X).First();

        // Place the sprite in the area
        sprite.Position = area.Location;

        // Area is now occupied
        remainingAreas.Remove(area);

        // Remember the remaining areas on the right and below for later
        AddOrMergeRect(new(area.X + sprite.Width, area.Y, area.Width - sprite.Width, sprite.Height));
        AddOrMergeRect(new(area.X, area.Y + sprite.Height, area.Width, area.Height - sprite.Height));
    }

    ableToFit = remainingSprites.Count == 0;
    if (!ableToFit)
        spriteSheetSize *= 2;
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
