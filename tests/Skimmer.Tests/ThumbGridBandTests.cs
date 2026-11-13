using System.Drawing;
using Xunit;

namespace Skimmer.Tests;

/// <summary>
/// Геометрия выделения рамкой (rubber band). Сетка виртуальная, поэтому попадания
/// считаются по прямоугольнику в координатах содержимого: проверяется, что рамка
/// задевает ровно те плитки, которые пересекает, не выходит за пределы видимой
/// области и не обращается к несуществующим элементам.
/// </summary>
public class ThumbGridBandTests
{
    private const int Count = 10;
    private const int Cols = 4;
    private const int CellW = 100;
    private const int CellH = 120;
    private const int OffX = 10;
    private const int ContentHeight = 360;   // 3 ряда
    private const int ViewWidth = 420;

    private static List<int> Hits(Rectangle band, int viewWidth = ViewWidth)
        => ThumbGrid.BandHits(band, Count, Cols, CellW, CellH, OffX, ContentHeight, viewWidth);

    [Fact]
    public void Band_OverFirstTwoTilesSelectsThem()
    {
        var band = Rectangle.FromLTRB(10, 10, 210, 110);

        Assert.Equal(new[] { 0, 1 }, Hits(band));
    }

    [Fact]
    public void VerticalStrip_SelectsOneTilePerRow()
    {
        // Ширина в один пиксель: при строго вертикальном перетаскивании рамка
        // всё равно должна выделять колонку, а не ничего.
        var band = Rectangle.FromLTRB(215, 0, 216, ContentHeight);

        Assert.Equal(new[] { 2, 6 }, Hits(band));
    }

    [Fact]
    public void Band_OverEmptyTailOfLastRowSelectsNothing()
    {
        // В последнем ряду всего два элемента: остальные ячейки пусты.
        var band = Rectangle.FromLTRB(210, 240, 410, ContentHeight);

        Assert.Empty(Hits(band));
    }

    [Fact]
    public void Band_InLeftAndRightGuttersSelectsNothing()
    {
        Assert.Empty(Hits(Rectangle.FromLTRB(-50, 0, 5, ContentHeight)));
        Assert.Empty(Hits(Rectangle.FromLTRB(412, 0, 420, ContentHeight)));
    }

    [Fact]
    public void EmptyBand_SelectsNothing()
    {
        Assert.Empty(Hits(new Rectangle(50, 50, 0, 0)));
    }

    [Fact]
    public void WholeContent_ReturnsEveryItemInReadingOrder()
    {
        var hits = Hits(Rectangle.FromLTRB(0, 0, ViewWidth, ContentHeight));

        Assert.Equal(Enumerable.Range(0, Count), hits);
        Assert.All(hits, i => Assert.InRange(i, 0, Count - 1));
    }

    [Fact]
    public void Band_IsClippedToTheVisibleWidth()
    {
        // Плитки за границей видимой области не выделяются, даже если рамка
        // формально их накрывает (сетка не прокручивается по горизонтали).
        var hits = Hits(Rectangle.FromLTRB(0, 0, 200, ContentHeight), viewWidth: 200);

        Assert.Equal(new[] { 0, 1, 4, 5, 8, 9 }, hits);
    }

    [Fact]
    public void EmptyFolder_SelectsNothing()
    {
        Assert.Empty(ThumbGrid.BandHits(Rectangle.FromLTRB(0, 0, ViewWidth, ContentHeight),
            0, Cols, CellW, CellH, OffX, 0, ViewWidth));
    }
}
