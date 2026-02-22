using System.Drawing;

class SpriteData
{
    public int Height => Bitmap.Height;
    public int Width => Bitmap.Width;
    public Size Size => Bitmap.Size;
    public int X => Position.X;
    public int Y => Position.Y;

    public required Bitmap Bitmap { get; set; }
    public Point Position { get; set; }
    public required string Name { get; set; }
}