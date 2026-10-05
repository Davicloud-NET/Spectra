namespace SoundCornersSpike.Grid;

// How a cell is called air or not.
internal enum CellRule
{
    // Air when the centre is not in solid.
    Center,

    // As Center, and two neighbours only join when the line between their
    // centres is clear.
    CenterAndLinks,

    // Air when the centre and the eight corners are not in solid.
    NinePoints,

    // Air when the box test finds no solid in the cell.
    Box,
}
