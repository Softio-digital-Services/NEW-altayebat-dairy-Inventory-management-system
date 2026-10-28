using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using InventorySystem.Data;
using InventorySystem.Helpers;
using Microsoft.Data.Sqlite;

namespace InventorySystem.Services
{
    public class ImportBatchResult
    {
        public int Imported { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public int Total => Imported + Updated + Skipped;
    }

    /// <summary>
    /// Shared CSV import/export mapping with column aliases and upsert logic.
    /// Used by web API and WinForms import paths.
    /// </summary>
    public static class CsvImportExportService
    {
        #region Row helpers

        public static Dictionary<string, string> RowFromDataRow(DataRow row)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (row?.Table == null) return dict;
            foreach (DataColumn col in row.Table.Columns)
                dict[col.ColumnName] = row[col]?.ToString()?.Trim() ?? "";
            return dict;
        }

        public static string Cell(Dictionary<string, string> row, params string[] keys)
        {
            if (row == null) return "";
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                foreach (var kv in row)
                {
                    if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(kv.Value))
                        return kv.Value.Trim();
                }
            }
            return "";
        }

        public static bool ParseBool(string value, bool defaultValue = false)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            var v = value.Trim().ToLowerInvariant();
            if (v is "1" or "true" or "yes" or "y" or "نعم") return true;
            if (v is "0" or "false" or "no" or "n" or "لا") return false;
            return defaultValue;
        }

        public static int ParseInt(string value, int defaultValue = 0)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : defaultValue;
        }

        public static decimal ParseDecimal(string value, decimal defaultValue = 0)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            // Tolerate values copied out of a formatted export, e.g. "$1,250.00".
            var v = value.Trim().Replace(",", "").TrimStart('$', '\u20AC', '\u00A3', '\u00A5').Trim();
            return decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : defaultValue;
        }

        /// <summary>True if the import file included this column (any alias).</summary>
        public static bool ColumnInRow(Dictionary<string, string> row, params string[] keys)
        {
            if (row == null || keys == null) return false;
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                foreach (var rk in row.Keys)
                    if (string.Equals(rk, key, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            return false;
        }

        private static string MergeString(string existing, Dictionary<string, string> row, params string[] keys)
        {
            if (!ColumnInRow(row, keys)) return existing;
            string v = Cell(row, keys);
            return string.IsNullOrWhiteSpace(v) ? existing : v;
        }

        private static int MergeInt(int existing, Dictionary<string, string> row, params string[] keys)
        {
            if (!ColumnInRow(row, keys)) return existing;
            string v = Cell(row, keys);
            return string.IsNullOrWhiteSpace(v) ? existing : ParseInt(v, existing);
        }

        private static decimal MergeDecimal(decimal existing, Dictionary<string, string> row, params string[] keys)
        {
            if (!ColumnInRow(row, keys)) return existing;
            string v = Cell(row, keys);
            return string.IsNullOrWhiteSpace(v) ? existing : ParseDecimal(v, existing);
        }

        private static bool MergeBool(bool existing, Dictionary<string, string> row, params string[] keys)
        {
            if (!ColumnInRow(row, keys)) return existing;
            string v = Cell(row, keys);
            return string.IsNullOrWhiteSpace(v) ? existing : ParseBool(v, existing);
        }

        private static PartData MergeProduct(PartData existing, PartData imported, Dictionary<string, string> row)
        {
            if (existing == null) return imported;
            var m = new PartData
            {
                Id = existing.Id,
                PartName = MergeString(existing.PartName, row, "part_name", "PartName", "name", "product_name", "product"),
                PartNumber = MergeString(existing.PartNumber, row, "part_number", "PartNumber", "sku", "SKU"),
                Description = MergeString(existing.Description, row, "description", "Description", "desc"),
                CategoryName = MergeString(existing.CategoryName, row, "category_name", "Category", "category"),
                SupplierName = MergeString(existing.SupplierName, row, "supplier_name", "supplier", "SupplierName"),
                QuantityInStock = MergeInt(existing.QuantityInStock, row, "quantity_in_stock", "Quantity", "stock", "qty"),
                MinimumStockLevel = MergeInt(existing.MinimumStockLevel, row, "minimum_stock_level", "MinimumStock", "min_stock", "minstock"),
                ReorderQuantity = MergeInt(existing.ReorderQuantity, row, "reorder_quantity", "ReorderQuantity", "reorder"),
                PurchasePrice = MergeDecimal(existing.PurchasePrice, row, "purchase_price", "cost", "Cost", "PurchasePrice"),
                SellingPrice = MergeDecimal(existing.SellingPrice, row, "selling_price", "UnitPrice", "price", "Price"),
                Location = MergeString(existing.Location, row, "location", "Location"),
                Shelf = MergeString(existing.Shelf, row, "shelf", "Shelf"),
                Barcode = MergeString(existing.Barcode, row, "barcode", "Barcode"),
                Status = MergeString(existing.Status, row, "status", "Status"),
                ItemType = MergeString(existing.ItemType, row, "item_type", "ItemType", "type"),
                UnitOfMeasure = MergeString(existing.UnitOfMeasure, row, "unit_of_measure", "uom", "Uom", "UOM"),
                BatchNumber = MergeString(existing.BatchNumber, row, "batch_number", "batch", "Batch", "BatchNumber"),
                ExpiryDate = MergeString(existing.ExpiryDate, row, "expiry_date", "expiry", "Expiry", "ExpiryDate"),
                IsSalesItem = MergeBool(existing.IsSalesItem, row, "is_sales_item", "sales_item", "IsSalesItem"),
                IsPurchaseItem = MergeBool(existing.IsPurchaseItem, row, "is_purchase_item", "purchase_item", "IsPurchaseItem"),
                IsInactive = MergeBool(existing.IsInactive, row, "is_inactive", "inactive", "IsInactive"),
                TaxRate = MergeDecimal(existing.TaxRate, row, "tax_rate", "tax", "TaxRate"),
                IsStockTracked = MergeBool(existing.IsStockTracked, row, "is_stock_tracked", "stock_tracked", "IsStockTracked"),
                SellByWeight = MergeBool(existing.SellByWeight, row, "sell_by_weight", "sellbyweight", "SellByWeight"),
                Price2 = MergeDecimal(existing.Price2, row, "price2", "Price2"),
                Price3 = MergeDecimal(existing.Price3, row, "price3", "Price3"),
                Price4 = MergeDecimal(existing.Price4, row, "price4", "Price4"),
                PartImage = existing.PartImage,
                SupplierId = existing.SupplierId
            };

            if (ColumnInRow(row, "part_image", "image", "Image", "PartImage", "imagepath"))
            {
                string img = Cell(row, "part_image", "image", "Image", "PartImage", "imagepath");
                if (!string.IsNullOrWhiteSpace(img))
                    m.PartImage = NormalizeImagePath(img);
            }

            if (ColumnInRow(row, "supplier_id", "supplierid"))
            {
                string supIdStr = Cell(row, "supplier_id", "supplierid");
                if (!string.IsNullOrWhiteSpace(supIdStr) && int.TryParse(supIdStr, out int supId) && supId > 0)
                    m.SupplierId = supId;
            }

            if (string.IsNullOrWhiteSpace(m.CategoryName)) m.CategoryName = existing.CategoryName ?? "General";
            if (m.IsInactive) m.Status = "Inactive";
            else if (!ColumnInRow(row, "status", "Status") && !ColumnInRow(row, "is_inactive", "inactive"))
                m.Status = existing.Status;

            return m;
        }

        #endregion

        #region Product mapping

        public static PartData MapProductRow(Dictionary<string, string> row)
        {
            if (row == null) return null;

            string name = Cell(row, "part_name", "PartName", "name", "product_name", "product");
            if (string.IsNullOrWhiteSpace(name)) return null;

            string status = Cell(row, "status", "Status");
            bool inactive = ParseBool(Cell(row, "is_inactive", "inactive", "IsInactive"));
            if (string.IsNullOrWhiteSpace(status))
                status = inactive ? "Inactive" : "Active";
            else if (inactive)
                status = "Inactive";

            bool sellByWeight = ParseBool(Cell(row, "sell_by_weight", "sellbyweight", "SellByWeight"));
            string uom = Cell(row, "unit_of_measure", "uom", "Uom", "UOM");
            string imageRaw = Cell(row, "part_image", "image", "Image", "PartImage", "imagepath");

            var part = new PartData
            {
                PartName = name,
                PartNumber = Cell(row, "part_number", "PartNumber", "sku", "SKU"),
                Description = Cell(row, "description", "Description", "desc"),
                CategoryName = Cell(row, "category_name", "Category", "category") is { Length: > 0 } cat ? cat : "General",
                SupplierName = Cell(row, "supplier_name", "supplier", "SupplierName"),
                QuantityInStock = ParseInt(Cell(row, "quantity_in_stock", "Quantity", "stock", "qty")),
                MinimumStockLevel = ParseInt(Cell(row, "minimum_stock_level", "MinimumStock", "min_stock", "minstock")),
                ReorderQuantity = ParseInt(Cell(row, "reorder_quantity", "ReorderQuantity", "reorder")),
                PurchasePrice = ParseDecimal(Cell(row, "purchase_price", "cost", "Cost", "PurchasePrice")),
                SellingPrice = ParseDecimal(Cell(row, "selling_price", "UnitPrice", "price", "Price")),
                Location = Cell(row, "location", "Location"),
                Shelf = Cell(row, "shelf", "Shelf"),
                Barcode = Cell(row, "barcode", "Barcode"),
                Status = status,
                ItemType = Cell(row, "item_type", "ItemType", "type") is { Length: > 0 } t ? t : "Product",
                UnitOfMeasure = uom,
                BatchNumber = Cell(row, "batch_number", "batch", "Batch", "BatchNumber"),
                ExpiryDate = Cell(row, "expiry_date", "expiry", "Expiry", "ExpiryDate"),
                IsSalesItem = ParseBool(Cell(row, "is_sales_item", "sales_item", "IsSalesItem"), true),
                IsPurchaseItem = ParseBool(Cell(row, "is_purchase_item", "purchase_item", "IsPurchaseItem")),
                IsInactive = inactive,
                TaxRate = ParseDecimal(Cell(row, "tax_rate", "tax", "TaxRate")),
                IsStockTracked = ParseBool(Cell(row, "is_stock_tracked", "stock_tracked", "IsStockTracked"), true),
                SellByWeight = sellByWeight,
                Price2 = ParseDecimal(Cell(row, "price2", "Price2")),
                Price3 = ParseDecimal(Cell(row, "price3", "Price3")),
                Price4 = ParseDecimal(Cell(row, "price4", "Price4"))
            };

            if (!string.IsNullOrWhiteSpace(imageRaw))
                part.PartImage = NormalizeImagePath(imageRaw);

            string supIdStr = Cell(row, "supplier_id", "supplierid");
            if (int.TryParse(supIdStr, out int supId) && supId > 0)
                part.SupplierId = supId;

            return part;
        }

        private static string NormalizeImagePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = path.Trim();
            if (path.StartsWith("/")) path = path.Substring(1);
            return path;
        }

        private static DateTime? ParseDateNullable(string raw)
        {
            string normalized = NormalizeDate(raw);
            if (string.IsNullOrWhiteSpace(normalized)) return null;
            if (DateTime.TryParse(normalized, out var dt)) return dt.Date;
            return null;
        }

        public static int? FindProductId(string partNumber, string barcode, string partName)
        {
            if (!string.IsNullOrWhiteSpace(partNumber))
            {
                var id = DatabaseHelper.ExecuteScalar<object>(
                    "SELECT id FROM parts WHERE part_number = @pn AND date_deleted IS NULL LIMIT 1",
                    new SqliteParameter("@pn", partNumber.Trim()));
                if (id != null && id != DBNull.Value) return Convert.ToInt32(id);
            }
            if (!string.IsNullOrWhiteSpace(barcode))
            {
                var id = DatabaseHelper.ExecuteScalar<object>(
                    "SELECT id FROM parts WHERE barcode = @bc AND date_deleted IS NULL LIMIT 1",
                    new SqliteParameter("@bc", barcode.Trim()));
                if (id != null && id != DBNull.Value) return Convert.ToInt32(id);
            }
            if (!string.IsNullOrWhiteSpace(partName))
            {
                var id = DatabaseHelper.ExecuteScalar<object>(
                    "SELECT id FROM parts WHERE part_name = @n AND date_deleted IS NULL LIMIT 1",
                    new SqliteParameter("@n", partName.Trim()));
                if (id != null && id != DBNull.Value) return Convert.ToInt32(id);
            }
            return null;
        }

        public static ImportBatchResult ImportProducts(IEnumerable<Dictionary<string, string>> rows)
        {
            // Accept our own exports in either language (and the old raw column names).
            rows = rows?.Select(r => ExportLocalizer.NormalizeRow(r, "products")).ToList();
            var result = new ImportBatchResult();
            var inventory = new InventoryService();

            foreach (var row in rows ?? Enumerable.Empty<Dictionary<string, string>>())
            {
                try
                {
                    var part = MapProductRow(row);
                    if (part == null) { result.Skipped++; continue; }

                    if (!part.SupplierId.HasValue && !string.IsNullOrWhiteSpace(part.SupplierName))
                    {
                        var supId = DatabaseHelper.ExecuteScalar<object>(
                            "SELECT id FROM suppliers WHERE supplier_name = @n AND date_deleted IS NULL LIMIT 1",
                            new SqliteParameter("@n", part.SupplierName));
                        if (supId != null && supId != DBNull.Value)
                            part.SupplierId = Convert.ToInt32(supId);
                    }

                    int? existingId = FindProductId(part.PartNumber, part.Barcode, part.PartName);
                    if (existingId.HasValue)
                    {
                        part.Id = existingId.Value;
                        var existing = PartData.GetById(existingId.Value);
                        if (existing != null)
                            part = MergeProduct(existing, part, row);

                        if (!part.SupplierId.HasValue && !string.IsNullOrWhiteSpace(part.SupplierName))
                        {
                            var supId = DatabaseHelper.ExecuteScalar<object>(
                                "SELECT id FROM suppliers WHERE supplier_name = @n AND date_deleted IS NULL LIMIT 1",
                                new SqliteParameter("@n", part.SupplierName));
                            if (supId != null && supId != DBNull.Value)
                                part.SupplierId = Convert.ToInt32(supId);
                        }

                        inventory.SaveProductService(part);
                        result.Updated++;
                    }
                    else
                    {
                        part.Id = 0;
                        inventory.SaveProductService(part);
                        result.Imported++;
                    }
                }
                catch (Exception ex)
                {
                    result.Skipped++;
                    result.Errors.Add(ex.Message);
                    ErrorLogger.LogError(ex, "CsvImportExportService.ImportProducts");
                }
            }

            return result;
        }

        #endregion

        #region Customer mapping

        public static ImportBatchResult ImportCustomers(IEnumerable<Dictionary<string, string>> rows)
        {
            // Accept our own exports in either language (and the old raw column names).
            rows = rows?.Select(r => ExportLocalizer.NormalizeRow(r, "customers")).ToList();
            var result = new ImportBatchResult();
            var service = new CustomerService();

            foreach (var row in rows ?? Enumerable.Empty<Dictionary<string, string>>())
            {
                try
                {
                    string name = Cell(row, "CustomerName", "customername", "name", "full_name", "customer");
                    if (string.IsNullOrWhiteSpace(name)) { result.Skipped++; continue; }

                    string phone = Cell(row, "Phone", "phone");
                    string email = Cell(row, "Email", "email");
                    string address = Cell(row, "Address", "address");
                    string city = Cell(row, "City", "city");
                    string postal = Cell(row, "PostalCode", "postalcode", "postal_code", "zip");
                    if (!string.IsNullOrWhiteSpace(city) || !string.IsNullOrWhiteSpace(postal))
                    {
                        address = (address ?? "") +
                            (string.IsNullOrWhiteSpace(city) ? "" : ", " + city) +
                            (string.IsNullOrWhiteSpace(postal) ? "" : " " + postal);
                    }
                    string type = Cell(row, "CustomerType", "customertype", "type") is { Length: > 0 } t ? t : "Regular";
                    decimal creditLimit = ParseDecimal(Cell(row, "CreditLimit", "creditlimit", "credit_limit"), 1000);
                    DateTime? dueDate = ParseDateNullable(Cell(row, "PaymentDueDate", "payment_due_date", "duedate", "due_date"));
                    int reminderDays = ParseInt(Cell(row, "ReminderDays", "reminder_days", "reminderdays"));
                    string balanceRaw = Cell(row, "Balance", "balance", "current_balance");
                    bool hasBalance = !string.IsNullOrWhiteSpace(balanceRaw);

                    var existingId = DatabaseHelper.ExecuteScalar<object>(
                        "SELECT customer_id FROM customers WHERE full_name = @n COLLATE NOCASE AND date_deleted IS NULL LIMIT 1",
                        new SqliteParameter("@n", name));

                    int customerId;
                    if (existingId != null && existingId != DBNull.Value)
                    {
                        customerId = Convert.ToInt32(existingId);
                        var exDt = DatabaseHelper.ExecuteDataTable(
                            @"SELECT full_name, phone, email, address, type, credit_limit, payment_due_date, reminder_days, current_balance
                              FROM customers WHERE customer_id = @id",
                            new SqliteParameter("@id", customerId));
                        var ex = exDt.Rows.Count > 0 ? exDt.Rows[0] : null;

                        string finalName = ex != null ? MergeString(ex["full_name"]?.ToString(), row, "CustomerName", "customername", "name", "full_name", "customer") : name;
                        string finalPhone = ex != null ? MergeString(ex["phone"]?.ToString(), row, "Phone", "phone") : phone;
                        string finalEmail = ex != null ? MergeString(ex["email"]?.ToString(), row, "Email", "email") : email;
                        string finalAddress = ex != null ? MergeString(ex["address"]?.ToString(), row, "Address", "address") : address;
                        if (ColumnInRow(row, "City", "city") || ColumnInRow(row, "PostalCode", "postalcode", "postal_code", "zip"))
                        {
                            string cityPart = ColumnInRow(row, "City", "city") ? Cell(row, "City", "city") : "";
                            string postalPart = ColumnInRow(row, "PostalCode", "postalcode", "postal_code", "zip") ? Cell(row, "PostalCode", "postalcode", "postal_code", "zip") : "";
                            if (!string.IsNullOrWhiteSpace(cityPart) || !string.IsNullOrWhiteSpace(postalPart))
                            {
                                finalAddress = (finalAddress ?? "") +
                                    (string.IsNullOrWhiteSpace(cityPart) ? "" : ", " + cityPart) +
                                    (string.IsNullOrWhiteSpace(postalPart) ? "" : " " + postalPart);
                            }
                        }
                        else if (!ColumnInRow(row, "Address", "address") && ex != null)
                        {
                            finalAddress = ex["address"]?.ToString();
                        }

                        string finalType = ex != null
                            ? (ColumnInRow(row, "CustomerType", "customertype", "type") && !string.IsNullOrWhiteSpace(Cell(row, "CustomerType", "customertype", "type"))
                                ? Cell(row, "CustomerType", "customertype", "type") : ex["type"]?.ToString())
                            : type;
                        decimal finalCredit = ex != null
                            ? MergeDecimal(ex["credit_limit"] == DBNull.Value ? 1000m : Convert.ToDecimal(ex["credit_limit"]), row, "CreditLimit", "creditlimit", "credit_limit")
                            : creditLimit;
                        DateTime? finalDue = dueDate;
                        if (ex != null)
                        {
                            if (ColumnInRow(row, "PaymentDueDate", "payment_due_date", "duedate", "due_date"))
                            {
                                string dueRaw = Cell(row, "PaymentDueDate", "payment_due_date", "duedate", "due_date");
                                finalDue = string.IsNullOrWhiteSpace(dueRaw)
                                    ? (ex["payment_due_date"] == DBNull.Value ? null : ParseDateNullable(ex["payment_due_date"]?.ToString()))
                                    : ParseDateNullable(dueRaw);
                            }
                            else
                                finalDue = ex["payment_due_date"] == DBNull.Value ? null : ParseDateNullable(ex["payment_due_date"]?.ToString());
                        }
                        int finalReminder = ex != null
                            ? MergeInt(ex["reminder_days"] == DBNull.Value ? 0 : Convert.ToInt32(ex["reminder_days"]), row, "ReminderDays", "reminder_days", "reminderdays")
                            : reminderDays;

                        service.UpdateCustomer(customerId, finalName, finalPhone, finalEmail, finalAddress, finalType ?? "Regular", finalCredit, finalDue, finalReminder);
                        result.Updated++;
                    }
                    else
                    {
                        customerId = service.AddCustomer(name, phone, email, address, type, creditLimit, dueDate, reminderDays);
                        result.Imported++;
                    }

                    if (ColumnInRow(row, "Balance", "balance", "current_balance") && hasBalance)
                    {
                        DatabaseHelper.ExecuteNonQuery(
                            "UPDATE customers SET current_balance = @b WHERE customer_id = @id",
                            new SqliteParameter("@b", ParseDecimal(balanceRaw)),
                            new SqliteParameter("@id", customerId));
                    }
                }
                catch (Exception ex)
                {
                    result.Skipped++;
                    result.Errors.Add(ex.Message);
                    ErrorLogger.LogError(ex, "CsvImportExportService.ImportCustomers");
                }
            }

            return result;
        }

        #endregion

        #region Supplier mapping

        public static ImportBatchResult ImportSuppliers(IEnumerable<Dictionary<string, string>> rows)
        {
            // Accept our own exports in either language (and the old raw column names).
            rows = rows?.Select(r => ExportLocalizer.NormalizeRow(r, "suppliers")).ToList();
            var result = new ImportBatchResult();
            var service = new SupplierService();

            foreach (var row in rows ?? Enumerable.Empty<Dictionary<string, string>>())
            {
                try
                {
                    string name = Cell(row, "SupplierName", "suppliername", "name", "supplier");
                    if (string.IsNullOrWhiteSpace(name)) { result.Skipped++; continue; }

                    string contact = Cell(row, "ContactPerson", "contactperson", "contact");
                    string email = Cell(row, "Email", "email");
                    string phone = Cell(row, "Phone", "phone");
                    string address = Cell(row, "Address", "address");
                    string city = Cell(row, "City", "city");
                    string postal = Cell(row, "PostalCode", "postalcode", "postal_code", "zip");
                    string website = Cell(row, "Website", "website");
                    string notes = Cell(row, "Notes", "notes");
                    string type = Cell(row, "type", "Type", "SupplierType", "suppliertype") is { Length: > 0 } t ? t : "Regular";
                    DateTime? dueDate = ParseDateNullable(Cell(row, "PaymentDueDate", "payment_due_date", "duedate", "due_date"));
                    int reminderDays = ParseInt(Cell(row, "ReminderDays", "reminder_days", "reminderdays"));
                    string balanceRaw = Cell(row, "Balance", "balance", "balance_due");
                    bool hasBalance = !string.IsNullOrWhiteSpace(balanceRaw);

                    string fullAddress = address ?? "";
                    if (!string.IsNullOrWhiteSpace(city)) fullAddress += (fullAddress.Length > 0 ? ", " : "") + city;
                    if (!string.IsNullOrWhiteSpace(postal)) fullAddress += (fullAddress.Length > 0 ? " " : "") + postal;
                    if (!string.IsNullOrWhiteSpace(website)) fullAddress += (fullAddress.Length > 0 ? " | " : "") + website;
                    if (!string.IsNullOrWhiteSpace(notes)) fullAddress += (fullAddress.Length > 0 ? " | " : "") + notes;

                    var existingId = DatabaseHelper.ExecuteScalar<object>(
                        "SELECT id FROM suppliers WHERE supplier_name = @n COLLATE NOCASE AND date_deleted IS NULL LIMIT 1",
                        new SqliteParameter("@n", name));

                    int supplierId;
                    if (existingId != null && existingId != DBNull.Value)
                    {
                        supplierId = Convert.ToInt32(existingId);
                        var exDt = DatabaseHelper.ExecuteDataTable(
                            @"SELECT supplier_name, contact_person, phone, email, address, type, payment_due_date, reminder_days, balance_due
                              FROM suppliers WHERE id = @id",
                            new SqliteParameter("@id", supplierId));
                        var ex = exDt.Rows.Count > 0 ? exDt.Rows[0] : null;

                        string finalName = ex != null ? MergeString(ex["supplier_name"]?.ToString(), row, "SupplierName", "suppliername", "name", "supplier") : name;
                        string finalPhone = ex != null ? MergeString(ex["phone"]?.ToString(), row, "Phone", "phone") : phone;
                        string finalEmail = ex != null ? MergeString(ex["email"]?.ToString(), row, "Email", "email") : email;
                        string finalContact = ex != null ? MergeString(ex["contact_person"]?.ToString(), row, "ContactPerson", "contactperson", "contact") : contact;

                        string finalAddress = fullAddress;
                        if (ex != null && !ColumnInRow(row, "Address", "address", "City", "city", "PostalCode", "postalcode", "Website", "website", "Notes", "notes"))
                            finalAddress = ex["address"]?.ToString() ?? fullAddress;
                        else if (ex != null && ColumnInRow(row, "Address", "address"))
                            finalAddress = MergeString(ex["address"]?.ToString(), row, "Address", "address");

                        string finalType = ex != null
                            ? MergeString(ex["type"]?.ToString() ?? "Regular", row, "type", "Type", "SupplierType", "suppliertype")
                            : type;
                        if (string.IsNullOrWhiteSpace(finalType)) finalType = "Regular";

                        DateTime? finalDue = dueDate;
                        if (ex != null)
                        {
                            if (ColumnInRow(row, "PaymentDueDate", "payment_due_date", "duedate", "due_date"))
                            {
                                string dueRaw = Cell(row, "PaymentDueDate", "payment_due_date", "duedate", "due_date");
                                finalDue = string.IsNullOrWhiteSpace(dueRaw)
                                    ? (ex["payment_due_date"] == DBNull.Value ? null : ParseDateNullable(ex["payment_due_date"]?.ToString()))
                                    : ParseDateNullable(dueRaw);
                            }
                            else
                                finalDue = ex["payment_due_date"] == DBNull.Value ? null : ParseDateNullable(ex["payment_due_date"]?.ToString());
                        }

                        int finalReminder = ex != null
                            ? MergeInt(ex["reminder_days"] == DBNull.Value ? 0 : Convert.ToInt32(ex["reminder_days"]), row, "ReminderDays", "reminder_days", "reminderdays")
                            : reminderDays;

                        service.UpdateSupplier(supplierId, finalName, finalPhone, finalEmail, finalAddress ?? "", finalType, finalDue, finalReminder);
                        if (!string.IsNullOrWhiteSpace(finalContact))
                        {
                            DatabaseHelper.ExecuteNonQuery(
                                "UPDATE suppliers SET contact_person = @c WHERE id = @id",
                                new SqliteParameter("@c", finalContact),
                                new SqliteParameter("@id", supplierId));
                        }
                        result.Updated++;
                    }
                    else
                    {
                        service.AddSupplier(name, phone, email, fullAddress, type, dueDate, reminderDays);
                        supplierId = DatabaseHelper.ExecuteScalar<int>(
                            "SELECT id FROM suppliers WHERE supplier_name = @n AND date_deleted IS NULL ORDER BY id DESC LIMIT 1",
                            new SqliteParameter("@n", name));
                        if (!string.IsNullOrWhiteSpace(contact))
                        {
                            DatabaseHelper.ExecuteNonQuery(
                                "UPDATE suppliers SET contact_person = @c WHERE id = @id",
                                new SqliteParameter("@c", contact),
                                new SqliteParameter("@id", supplierId));
                        }
                        result.Imported++;
                    }

                    if (ColumnInRow(row, "Balance", "balance", "balance_due") && hasBalance)
                    {
                        DatabaseHelper.ExecuteNonQuery(
                            "UPDATE suppliers SET balance_due = @b WHERE id = @id",
                            new SqliteParameter("@b", ParseDecimal(balanceRaw)),
                            new SqliteParameter("@id", supplierId));
                    }
                }
                catch (Exception ex)
                {
                    result.Skipped++;
                    result.Errors.Add(ex.Message);
                    ErrorLogger.LogError(ex, "CsvImportExportService.ImportSuppliers");
                }
            }

            return result;
        }

        #endregion

        #region Expense mapping

        public static ImportBatchResult ImportExpenses(IEnumerable<Dictionary<string, string>> rows, string recordedBy = "Import")
        {
            // Accept our own exports in either language (and the old raw column names).
            rows = rows?.Select(r => ExportLocalizer.NormalizeRow(r, "expenses")).ToList();
            var result = new ImportBatchResult();

            foreach (var row in rows ?? Enumerable.Empty<Dictionary<string, string>>())
            {
                try
                {
                    string category = Cell(row, "category", "Category");
                    decimal amount = ParseDecimal(Cell(row, "amount", "Amount"));
                    if (string.IsNullOrWhiteSpace(category) || amount <= 0) { result.Skipped++; continue; }

                    string dateRaw = Cell(row, "date", "expensedate", "expense_date", "ExpenseDate");
                    string expenseDate = NormalizeDate(dateRaw) ?? DateTime.Now.ToString("yyyy-MM-dd");
                    string description = Cell(row, "description", "desc", "Description");
                    bool isPaid = ParseBool(Cell(row, "paid", "is_paid", "ispaid"), false);
                    bool isRecurring = ParseBool(Cell(row, "recurring", "is_recurring", "isrecurring"), false);

                    // Date + category + amount + description is the natural key, so re-importing
                    // an exported file updates the existing rows instead of duplicating them.
                    int existingId = DatabaseHelper.ExecuteScalar<int>(
                        @"SELECT COALESCE(MAX(expense_id), 0) FROM expenses
                          WHERE date_deleted IS NULL
                            AND date(expense_date) = date(@d)
                            AND category = @c
                            AND amount = @a
                            AND COALESCE(description, '') = @desc",
                        new SqliteParameter("@c", category),
                        new SqliteParameter("@d", expenseDate),
                        new SqliteParameter("@a", amount),
                        new SqliteParameter("@desc", description ?? ""));

                    if (existingId > 0)
                    {
                        DatabaseHelper.ExecuteNonQuery(
                            @"UPDATE expenses SET is_paid = @paid, is_recurring = @rec WHERE expense_id = @id",
                            new SqliteParameter("@paid", isPaid ? 1 : 0),
                            new SqliteParameter("@rec", isRecurring ? 1 : 0),
                            new SqliteParameter("@id", existingId));
                        result.Updated++;
                    }
                    else
                    {
                        DatabaseHelper.ExecuteNonQuery(
                            @"INSERT INTO expenses (category, expense_date, amount, description, recorded_by, is_paid, is_recurring)
                              VALUES (@c, @d, @a, @desc, @u, @paid, @rec)",
                            new SqliteParameter("@c", category),
                            new SqliteParameter("@d", expenseDate),
                            new SqliteParameter("@a", amount),
                            new SqliteParameter("@desc", description),
                            new SqliteParameter("@u", recordedBy),
                            new SqliteParameter("@paid", isPaid ? 1 : 0),
                            new SqliteParameter("@rec", isRecurring ? 1 : 0));
                        result.Imported++;
                    }
                }
                catch (Exception ex)
                {
                    result.Skipped++;
                    result.Errors.Add(ex.Message);
                    ErrorLogger.LogError(ex, "CsvImportExportService.ImportExpenses");
                }
            }

            return result;
        }

        private static string NormalizeDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (raw.Contains('T')) raw = raw.Split('T')[0];
            if (DateTime.TryParse(raw, out var dt))
                return dt.ToString("yyyy-MM-dd");
            return null;
        }

        #endregion

        #region Export tables

        // Every builder returns its columns in page order with English/Arabic headers and
        // translated coded values (see ExportLocalizer). lang = "ar" | "en".

        private static DataTable MarkCurrency(DataTable dt, params string[] columns) =>
            ImportExportHelper.MarkCurrencyColumns(dt, columns);

        private static DataTable MarkFormat(DataTable dt, string format, params string[] columns)
        {
            if (dt == null) return dt;
            foreach (string name in columns)
                if (dt.Columns.Contains(name))
                    dt.Columns[name].ExtendedProperties[ImportExportHelper.NumberFormatKey] = format;
            return dt;
        }

        /// <summary>Whole numbers as "1,250"; anything with decimals (litres, kg) as "1,250.5" / "0.525".</summary>
        private static string QuantityFormatFor(IEnumerable<decimal> values) =>
            values.All(v => v == decimal.Truncate(v)) ? "#,##0" : "#,##0.0##";

        /// <summary>Gives quantity columns a number format that suits their values (no trailing "200.").</summary>
        private static DataTable MarkQuantity(DataTable dt, params string[] columns)
        {
            if (dt == null) return dt;
            foreach (string name in columns)
            {
                if (!dt.Columns.Contains(name)) continue;
                var values = dt.Rows.Cast<DataRow>()
                    .Where(r => r[name] != DBNull.Value)
                    .Select(r => decimal.TryParse(Convert.ToString(r[name], CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m);
                dt.Columns[name].ExtendedProperties[ImportExportHelper.NumberFormatKey] = QuantityFormatFor(values);
            }
            return dt;
        }

        /// <summary>SQL fragment rendering a 0/1 flag as Yes/No for export readability.</summary>
        private static string YesNo(string expression, string alias) =>
            $"CASE WHEN COALESCE({expression}, 0) IN (1, '1') THEN 'Yes' ELSE 'No' END AS {alias}";

        private static readonly HashSet<string> WeightUnits = new(StringComparer.OrdinalIgnoreCase)
            { "kg", "g", "gram", "grams", "kilo", "kilos", "kilogram", "kilograms", "lb", "lbs", "oz" };

        /// <summary>Full inventory (re-importable). Adds "Stock (as shown)": weight items in kg like the POS shows them.</summary>
        public static DataTable BuildProductsExportTable(bool includeInactive = true, string lang = "en")
        {
            bool ar = ExportLocalizer.IsArabic(lang);
            string statusFilter = includeInactive
                ? "WHERE p.date_deleted IS NULL"
                : "WHERE p.date_deleted IS NULL AND (p.status = 'Active' OR p.status IS NULL) AND COALESCE(p.is_inactive, 0) = 0";

            var dt = DatabaseHelper.ExecuteDataTable($@"
                SELECT p.part_number, p.part_name, p.description,
                       COALESCE(c.category_name, 'General') AS category_name,
                       p.supplier_id, COALESCE(s.supplier_name, '') AS supplier_name,
                       p.quantity_in_stock, p.selling_price, p.purchase_price,
                       p.minimum_stock_level, p.reorder_quantity, p.location, p.shelf, p.barcode,
                       p.unit_of_measure, p.batch_number, p.expiry_date, p.item_type,
                       CASE WHEN COALESCE(p.is_sales_item, 1) IN (1, '1') THEN 'Yes' ELSE 'No' END AS is_sales_item,
                       {YesNo("p.is_purchase_item", "is_purchase_item")},
                       {YesNo("p.is_inactive", "is_inactive")},
                       COALESCE(p.tax_rate, 0) AS tax_rate,
                       CASE WHEN COALESCE(p.is_stock_tracked, 1) IN (1, '1') THEN 'Yes' ELSE 'No' END AS is_stock_tracked,
                       {YesNo("p.sell_by_weight", "sell_by_weight")},
                       COALESCE(p.price2, 0) AS price2,
                       COALESCE(p.price3, 0) AS price3,
                       COALESCE(p.price4, 0) AS price4,
                       COALESCE(p.part_image, '') AS part_image,
                       COALESCE(p.status, 'Active') AS status
                FROM parts p
                LEFT JOIN categories c ON p.category_id = c.id
                LEFT JOIN suppliers s ON p.supplier_id = s.id
                {statusFilter}
                ORDER BY c.category_name, p.part_name");

            // Weight items keep their stock in grams; show it the way the POS does (12.000 kg).
            dt.Columns.Add("stock_display", typeof(string));
            foreach (DataRow r in dt.Rows)
            {
                decimal qty = r["quantity_in_stock"] == DBNull.Value ? 0 : Convert.ToDecimal(r["quantity_in_stock"]);
                string uom = r["unit_of_measure"]?.ToString()?.Trim() ?? "";
                bool byWeight = string.Equals(r["sell_by_weight"]?.ToString(), "Yes", StringComparison.OrdinalIgnoreCase)
                                || WeightUnits.Contains(uom);
                r["stock_display"] = byWeight
                    ? (qty / 1000m).ToString("0.000", CultureInfo.InvariantCulture) + (ar ? " كغ" : " kg")
                    : qty.ToString("0.###", CultureInfo.InvariantCulture) + " " + (string.IsNullOrEmpty(uom)
                        ? (ar ? "قطعة" : "pcs")
                        : ExportLocalizer.Value(uom, ar));
            }

            MarkCurrency(dt, "selling_price", "purchase_price", "price2", "price3", "price4");
            return ExportLocalizer.Localize(dt, "products", ar);
        }

        public static DataTable BuildCustomersExportTable(string lang = "en")
        {
            var dt = MarkCurrency(DatabaseHelper.ExecuteDataTable(@"
                SELECT full_name AS CustomerName, phone AS Phone, email AS Email, address AS Address,
                       COALESCE(type, 'Regular') AS CustomerType, COALESCE(current_balance, 0) AS Balance,
                       COALESCE(credit_limit, 1000) AS CreditLimit,
                       payment_due_date AS PaymentDueDate, COALESCE(reminder_days, 0) AS ReminderDays
                FROM customers
                WHERE date_deleted IS NULL
                ORDER BY full_name"),
                "Balance", "CreditLimit");
            return ExportLocalizer.Localize(dt, "customers", ExportLocalizer.IsArabic(lang));
        }

        /// <summary>Suppliers (re-importable). City/PostalCode/Website/Notes are never stored, so they are not written.</summary>
        public static DataTable BuildSuppliersExportTable(string lang = "en")
        {
            var dt = MarkCurrency(DatabaseHelper.ExecuteDataTable(@"
                SELECT supplier_name AS SupplierName, contact_person AS ContactPerson,
                       phone AS Phone, email AS Email, address AS Address,
                       COALESCE(type, 'Regular') AS Type, COALESCE(balance_due, 0) AS Balance,
                       payment_due_date AS PaymentDueDate, COALESCE(reminder_days, 0) AS ReminderDays
                FROM suppliers
                WHERE date_deleted IS NULL
                ORDER BY supplier_name"),
                "Balance");
            return ExportLocalizer.Localize(dt, "suppliers", ExportLocalizer.IsArabic(lang));
        }

        /// <summary>Parses "1,2,3" into ids; anything that is not a number is ignored.</summary>
        private static List<long> ParseIdList(string ids)
        {
            var list = new List<long>();
            foreach (var part in (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) list.Add(id);
            return list;
        }

        /// <summary>
        /// Expenses with everything the page shows (deliverer, liters, price per liter, milk type, shift).
        /// ids = the expenses currently listed on the page (its filters); empty = all.
        /// </summary>
        public static DataTable BuildExpensesExportTable(string ids = null, string lang = "en")
        {
            var idList = ParseIdList(ids);
            string idFilter = idList.Count > 0 ? $" AND e.expense_id IN ({string.Join(",", idList)})" : "";
            var dt = DatabaseHelper.ExecuteDataTable($@"
                SELECT e.expense_date AS date, e.category,
                       COALESCE(s.supplier_name, '') AS deliverer,
                       CASE WHEN COALESCE(e.liters, 0) > 0 THEN e.liters END AS liters,
                       CASE WHEN COALESCE(e.unit_price, 0) > 0 THEN e.unit_price END AS unit_price,
                       COALESCE(e.milk_type, '') AS milk_type, COALESCE(e.shift, '') AS shift,
                       e.amount, COALESCE(e.description, '') AS description,
                       {YesNo("e.is_paid", "paid")},
                       {YesNo("e.is_recurring", "recurring")},
                       COALESCE(e.recorded_by, '') AS recorded_by
                FROM expenses e
                LEFT JOIN suppliers s ON s.id = e.supplier_id
                WHERE e.date_deleted IS NULL AND e.category != 'System'{idFilter}
                ORDER BY e.expense_date DESC, e.expense_id DESC");
            MarkCurrency(dt, "unit_price", "amount");
            MarkQuantity(dt, "liters");
            return ExportLocalizer.Localize(dt, "expenses", ExportLocalizer.IsArabic(lang));
        }

        /// <summary>
        /// Completed sales in the page's date range (both empty = all), with the items sold, payment,
        /// balance due and shipping details. Column keys match SaleImportRow so an export can be re-imported.
        /// </summary>
        public static DataTable BuildSalesExportTable(string from = null, string to = null, string lang = "en")
        {
            bool ranged = DateTime.TryParse(from, out var f) & DateTime.TryParse(to, out var t);
            if (ranged && t < f) (f, t) = (t, f);
            string range = ranged ? " AND date(o.order_date) BETWEEN date(@from) AND date(@to)" : "";
            string sql = $@"
                SELECT o.order_id AS orderId,
                       o.order_date AS date,
                       COALESCE(c.full_name, 'Walk-in') AS customer,
                       (SELECT COALESCE(SUM(oi.quantity), 0) FROM order_items oi
                         WHERE oi.order_id = o.order_id) AS itemCount,
                       (SELECT GROUP_CONCAT(printf('%s × %g', COALESCE(NULLIF(oi.item_name, ''), p.part_name, '?'), oi.quantity), ' | ')
                          FROM order_items oi LEFT JOIN parts p ON p.id = oi.part_id
                         WHERE oi.order_id = o.order_id) AS items,
                       COALESCE(o.total_amount, 0) AS total,
                       COALESCE(o.amount_paid, 0) AS amountPaid,
                       MAX(COALESCE(o.total_amount, 0) - COALESCE(o.amount_paid, 0), 0) AS balanceDue,
                       COALESCE(o.payment_status, 'Paid') AS payment,
                       COALESCE(o.payment_method, '') AS paymentMethod,
                       COALESCE(o.status, '') AS status,
                       COALESCE(o.shipping_address, '') AS shippingAddress,
                       o.delivery_date AS deliveryDate,
                       o.due_date AS dueDate
                FROM orders o
                LEFT JOIN customers c ON o.customer_id = c.customer_id
                WHERE o.status = 'Completed'{range}
                ORDER BY o.order_date DESC, o.order_id DESC";
            var dt = ranged
                ? DatabaseHelper.ExecuteDataTable(sql,
                    new SqliteParameter("@from", f.ToString("yyyy-MM-dd")),
                    new SqliteParameter("@to", t.ToString("yyyy-MM-dd")))
                : DatabaseHelper.ExecuteDataTable(sql);
            MarkCurrency(dt, "total", "amountPaid", "balanceDue");
            MarkQuantity(dt, "itemCount");
            return ExportLocalizer.Localize(dt, "sales", ExportLocalizer.IsArabic(lang));
        }

        /// <summary>
        /// The History page's data for its selected type (orders, pay later, inventory log, customers,
        /// suppliers, quotations) — the same query the page uses.
        /// </summary>
        public static DataTable BuildHistoryExportTable(string kind = "all", string lang = "en")
        {
            var svc = new HistoryService();
            DataTable dt = (kind ?? "").ToLowerInvariant() switch
            {
                "inventory" => svc.GetInventoryLogs(),
                "customers" => svc.GetCustomerHistory(),
                "pay_later" => svc.GetOrderHistory(unpaidOnly: true),
                "quotations" => svc.GetQuotationHistory(),
                "suppliers" => svc.GetSupplierHistory(),
                _ => svc.GetOrderHistory()
            };
            foreach (DataColumn col in dt.Columns)
                col.ColumnName = col.ColumnName.Trim().ToLowerInvariant().Replace(' ', '_');
            MarkCurrency(dt, "total", "amount");
            return ExportLocalizer.Localize(dt, "history", ExportLocalizer.IsArabic(lang));
        }

        private static decimal Dec(DataRow r, string col) =>
            r == null || !r.Table.Columns.Contains(col) || r[col] == DBNull.Value ? 0m : Convert.ToDecimal(r[col], CultureInfo.InvariantCulture);

        /// <summary>
        /// The whole Reports page for a period, one sheet per section in page order: summary
        /// (money, activity, milk), sales vs expenses trend, products, categories, expenses,
        /// expenses by category, milk deliverers.
        /// </summary>
        public static List<(string Sheet, DataTable Table)> BuildReportExportTables(DateTime fromDate, DateTime toDate, string lang = "en")
        {
            bool ar = ExportLocalizer.IsArabic(lang);
            var svc = new ReportService();
            var s = svc.GetSummary(fromDate, toDate);
            var act = svc.GetActivity(fromDate, toDate);
            var sheets = new List<(string, DataTable)>();

            // Summary: metric | value, each row with its own number format.
            var summary = new DataTable();
            summary.Columns.Add("metric", typeof(string));
            summary.Columns.Add("value", typeof(object));
            var formats = new List<string>();
            void Add(string key, object value, string format)
            {
                summary.Rows.Add(ExportLocalizer.Metric(key, ar), value);
                formats.Add(format);
            }
            const string money = ImportExportHelper.CurrencyFormat;
            Add("date_from", s.FromDate.Date, "yyyy-mm-dd");
            Add("date_to", s.ToDate.Date, "yyyy-mm-dd");
            Add("total_sales", s.TotalSales, money);
            Add("total_cost", s.TotalCost, money);
            Add("profit_before", s.TotalProfit, money);
            Add("total_expenses", s.TotalExpenses, money);
            Add("milk_expenses", s.TotalMilkExpenses, money);
            Add("general_expenses", s.TotalGeneralExpenses, money);
            Add("profit_after", s.TotalProfitAfterExpenses, money);
            Add("order_count", Dec(act, "order_count"), "#,##0");
            Add("items_sold", Dec(act, "items_sold"), QuantityFormatFor(new[] { Dec(act, "items_sold") }));
            Add("unpaid_count", Dec(act, "unpaid_count"), "#,##0");
            Add("unpaid_total", Dec(act, "unpaid_total"), money);
            Add("returns_count", Dec(act, "returns_count"), "#,##0");
            Add("returns_total", Dec(act, "returns_total"), money);
            Add("milk_liters", s.TotalMilkLiters, QuantityFormatFor(new[] { s.TotalMilkLiters }));
            Add("milk_unpaid", s.TotalUnpaidMilkExpenses, money);
            Add("expense_count", Dec(act, "expense_count"), "#,##0");
            summary.Columns["value"].ExtendedProperties[ImportExportHelper.RowNumberFormatsKey] = formats.ToArray();
            sheets.Add((ExportLocalizer.Sheet("report_summary", ar), ExportLocalizer.Localize(summary, "report_summary", ar)));

            // Sales vs expenses per day (per month for long ranges), like the chart.
            var trend = new DataTable();
            trend.Columns.Add("period", typeof(string));
            trend.Columns.Add("sales", typeof(decimal));
            trend.Columns.Add("expenses", typeof(decimal));
            trend.Columns.Add("net", typeof(decimal));
            foreach (var item in svc.GetTrend(fromDate, toDate))
            {
                var type = item.GetType();
                string period = type.GetProperty("period")?.GetValue(item)?.ToString() ?? "";
                decimal sales = Convert.ToDecimal(type.GetProperty("sales")?.GetValue(item) ?? 0m);
                decimal expenses = Convert.ToDecimal(type.GetProperty("expenses")?.GetValue(item) ?? 0m);
                trend.Rows.Add(period, sales, expenses, sales - expenses);
            }
            MarkCurrency(trend, "sales", "expenses", "net");
            sheets.Add((ExportLocalizer.Sheet("report_trend", ar), ExportLocalizer.Localize(trend, "report_trend", ar)));

            // Products sold (the page's query) + margin.
            var products = svc.GetProductsReport(fromDate, toDate);
            products.Columns.Add("margin", typeof(decimal));
            var weight = new DataColumn("sell_by_weight_text", typeof(string));
            products.Columns.Add(weight);
            foreach (DataRow r in products.Rows)
            {
                decimal sales = Dec(r, "total_sales");
                r["margin"] = sales > 0 ? Math.Round(Dec(r, "profit") / sales * 100m, 1) : 0m;
                r["sell_by_weight_text"] = Dec(r, "sell_by_weight") != 0 ? "Yes" : "No";
            }
            products.Columns.Remove("sell_by_weight");
            weight.ColumnName = "sell_by_weight";
            MarkCurrency(products, "unit_price", "total_sales", "total_cost", "profit");
            MarkQuantity(products, "quantity_sold");
            MarkFormat(products, "0.0", "margin");
            sheets.Add((ExportLocalizer.Sheet("report_products", ar), ExportLocalizer.Localize(products, "report_products", ar)));

            // Sales by category + share of sales.
            var cats = svc.GetTopSellingCategories(fromDate, toDate, 100000);
            cats.Columns.Add("share", typeof(decimal));
            decimal catTotal = cats.Rows.Cast<DataRow>().Sum(r => Dec(r, "total_sales"));
            foreach (DataRow r in cats.Rows)
                r["share"] = catTotal > 0 ? Math.Round(Dec(r, "total_sales") / catTotal * 100m, 1) : 0m;
            MarkCurrency(cats, "total_sales", "profit");
            MarkQuantity(cats, "quantity_sold", "kg_sold");
            MarkFormat(cats, "0.0", "share");
            sheets.Add((ExportLocalizer.Sheet("report_categories", ar), ExportLocalizer.Localize(cats, "report_categories", ar)));

            // Every expense in the period.
            var exp = svc.GetExpensesList(fromDate, toDate);
            var paidText = new DataColumn("is_paid_text", typeof(string));
            exp.Columns.Add(paidText);
            foreach (DataRow r in exp.Rows) r["is_paid_text"] = Dec(r, "is_paid") != 0 ? "Yes" : "No";
            exp.Columns.Remove("is_paid");
            if (exp.Columns.Contains("expense_id")) exp.Columns.Remove("expense_id");
            paidText.ColumnName = "is_paid";
            MarkCurrency(exp, "amount");
            MarkQuantity(exp, "liters");
            sheets.Add((ExportLocalizer.Sheet("report_expenses", ar), ExportLocalizer.Localize(exp, "report_expenses", ar)));

            var expCats = svc.GetExpenseCategoryBreakdown(fromDate, toDate);
            MarkCurrency(expCats, "total_amount", "paid_amount", "unpaid_amount");
            sheets.Add((ExportLocalizer.Sheet("report_expcats", ar), ExportLocalizer.Localize(expCats, "report_expcats", ar)));

            var milk = svc.GetMilkSupplierBreakdown(fromDate, toDate);
            if (milk.Columns.Contains("supplier_id")) milk.Columns.Remove("supplier_id");
            MarkCurrency(milk, "avg_unit_price", "total_amount", "total_paid", "total_unpaid");
            MarkQuantity(milk, "total_liters");
            sheets.Add((ExportLocalizer.Sheet("report_milk", ar), ExportLocalizer.Localize(milk, "report_milk", ar)));

            return sheets;
        }

        /// <summary>
        /// Milk Processing page: raw milk received (used / remaining) and what was made, following
        /// the page's filters — show fully used, milk type, supplier.
        /// </summary>
        public static List<(string Sheet, DataTable Table)> BuildMilkExportTables(bool includeFinished, string milkType, string supplier, string lang = "en")
        {
            bool ar = ExportLocalizer.IsArabic(lang);
            var svc = new MilkProductionService();
            bool typeAll = string.IsNullOrEmpty(milkType) || string.Equals(milkType, "all", StringComparison.OrdinalIgnoreCase);
            bool suppAll = string.IsNullOrEmpty(supplier);

            var intakes = svc.GetMilkIntakes(onlyRemaining: !includeFinished);
            foreach (DataRow r in intakes.Select().ToList())
            {
                bool okType = typeAll || string.Equals(r["milk_type"]?.ToString() ?? "", milkType, StringComparison.OrdinalIgnoreCase);
                bool okSupp = suppAll || string.Equals(r["supplier_name"]?.ToString() ?? "", supplier, StringComparison.OrdinalIgnoreCase);
                if (!okType || !okSupp) r.Delete();
            }
            intakes.AcceptChanges();
            foreach (string drop in new[] { "expense_id", "description" })
                if (intakes.Columns.Contains(drop)) intakes.Columns.Remove(drop);
            MarkQuantity(intakes, "liters", "used", "remaining");

            var prods = svc.GetProductions(null);
            foreach (DataRow r in prods.Select().ToList())
                if (!suppAll && !string.Equals(r["supplier_name"]?.ToString() ?? "", supplier, StringComparison.OrdinalIgnoreCase))
                    r.Delete();
            prods.AcceptChanges();
            foreach (string drop in new[] { "production_id", "expense_id", "recorded_by", "by_weight" })
                if (prods.Columns.Contains(drop)) prods.Columns.Remove(drop);
            MarkQuantity(prods, "liters_used", "quantity_made");

            return new List<(string, DataTable)>
            {
                (ExportLocalizer.Sheet("milk_intakes", ar), ExportLocalizer.Localize(intakes, "milk_intakes", ar)),
                (ExportLocalizer.Sheet("milk_productions", ar), ExportLocalizer.Localize(prods, "milk_productions", ar)),
            };
        }

        #endregion
    }
}
