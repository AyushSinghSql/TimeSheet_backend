using NPOI.SS.UserModel;
using System.Globalization;

namespace TimeSheet.DTOs
{
    public class ExcelHelper
    {
        public string GetString(ICell cell)
        {
            if (cell == null) return "";
            return cell.CellType switch
            {
                CellType.String => cell.StringCellValue,
                CellType.Numeric => cell.NumericCellValue.ToString(),
                _ => cell.ToString().Trim()
            };
        }

        public decimal GetDecimal(ICell cell)
        {
            if (cell == null) return 0;
            if (cell.CellType == CellType.Numeric) return (decimal)cell.NumericCellValue;

            decimal.TryParse(cell.ToString().Trim(), out var v);
            return v;
        }

        public int GetInt(ICell cell)
        {
            if (cell == null) return 0;
            if (cell.CellType == CellType.Numeric) return (int)cell.NumericCellValue;

            int.TryParse(cell.ToString().Trim(), out var v);
            return v;
        }

        public DateOnly? GetDate(ICell cell)
        {
            if (cell == null) return null;

            if (cell.CellType == CellType.Numeric && DateUtil.IsCellDateFormatted(cell))
                return DateOnly.FromDateTime(cell.DateCellValue.GetValueOrDefault());

            string val = cell.ToString();
            if (string.IsNullOrWhiteSpace(val)) return null;

            // Try normal date
            if (DateOnly.TryParse(val, out var d))
                return d;

            // Try MM-dd-yy (your format: 11-07-25)
            if (DateOnly.TryParseExact(val, "MM-dd-yy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
                return d2;

            return null;
        }

        public string RowToCsv(IRow row)
        {
            List<string> cells = new List<string>();

            int lastCellNum = row.LastCellNum; // Get total number of cells in row

            for (int i = 0; i < lastCellNum; i++)
            {
                var cell = row.GetCell(i);

                string value = cell == null ? "" : cell.ToString().Trim();

                // Escape values for CSV
                if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
                {
                    value = "\"" + value.Replace("\"", "\"\"") + "\"";
                }

                cells.Add(value);
            }

            return string.Join(",", cells);
        }


    }
}
