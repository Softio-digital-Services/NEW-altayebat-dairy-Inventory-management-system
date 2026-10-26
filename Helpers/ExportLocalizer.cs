using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace InventorySystem.Helpers
{
    /// <summary>
    /// Column labels, coded-value translations and sheet names for the web Excel/CSV exports,
    /// in English and Arabic. Chosen per request (?lang=ar|en) — deliberately independent of
    /// LocalizationManager, which is global desktop state and must not be switched by web calls.
    ///
    /// Each export "kind" lists its columns in page order as (canonical key, English, Arabic).
    /// The same tables map labels back to canonical keys on import, so an exported file
    /// (English or Arabic) can always be re-imported.
    /// </summary>
    public static class ExportLocalizer
    {
        public static bool IsArabic(string lang) =>
            string.Equals(lang?.Trim(), "ar", StringComparison.OrdinalIgnoreCase);

        private static (string Key, string En, string Ar) C(string key, string en, string ar) => (key, en, ar);

        /// <summary>Columns per export kind, in the order they are written.</summary>
        public static readonly Dictionary<string, (string Key, string En, string Ar)[]> Kinds =
            new(StringComparer.OrdinalIgnoreCase)
        {
            ["products"] = new[]
            {
                C("part_number", "Product Code", "رمز المنتج"),
                C("part_name", "Product Name", "اسم المنتج"),
                C("description", "Description", "الوصف"),
                C("category_name", "Category", "الفئة"),
                C("supplier_id", "Supplier ID", "رقم المورد"),
                C("supplier_name", "Supplier", "المورد"),
                C("quantity_in_stock", "Quantity in Stock", "الكمية في المخزون"),
                C("stock_display", "Stock (as shown)", "المخزون (كما يظهر)"),
                C("selling_price", "Selling Price", "سعر البيع"),
                C("purchase_price", "Purchase Price", "سعر الشراء"),
                C("minimum_stock_level", "Minimum Stock", "الحد الأدنى للمخزون"),
                C("reorder_quantity", "Reorder Quantity", "كمية إعادة الطلب"),
                C("location", "Location", "الموقع"),
                C("shelf", "Shelf", "الرف"),
                C("barcode", "Barcode", "الباركود"),
                C("unit_of_measure", "Unit", "الوحدة"),
                C("batch_number", "Batch Number", "رقم الدفعة"),
                C("expiry_date", "Expiry Date", "تاريخ الانتهاء"),
                C("item_type", "Item Type", "نوع الصنف"),
                C("is_sales_item", "Sales Item", "صنف بيع"),
                C("is_purchase_item", "Purchase Item", "صنف شراء"),
                C("is_inactive", "Inactive", "غير نشط"),
                C("tax_rate", "Tax Rate %", "نسبة الضريبة %"),
                C("is_stock_tracked", "Track Stock", "تتبع المخزون"),
                C("sell_by_weight", "Sold by Weight", "يباع بالوزن"),
                C("price2", "Price 2", "السعر 2"),
                C("price3", "Price 3", "السعر 3"),
                C("price4", "Price 4", "السعر 4"),
                C("part_image", "Image", "الصورة"),
                C("status", "Status", "الحالة"),
            },
            ["customers"] = new[]
            {
                C("CustomerName", "Customer Name", "اسم الزبون"),
                C("Phone", "Phone", "الهاتف"),
                C("Email", "Email", "البريد الإلكتروني"),
                C("Address", "Address", "العنوان"),
                C("CustomerType", "Customer Type", "نوع الزبون"),
                C("Balance", "Amount Owed", "المبلغ المستحق"),
                C("CreditLimit", "Credit Limit", "حد الائتمان"),
                C("PaymentDueDate", "Payment Due Date", "تاريخ استحقاق الدفع"),
                C("ReminderDays", "Reminder Days", "أيام التذكير"),
            },
            ["suppliers"] = new[]
            {
                C("SupplierName", "Supplier Name", "اسم المورد"),
                C("ContactPerson", "Contact Person", "جهة الاتصال"),
                C("Phone", "Phone", "الهاتف"),
                C("Email", "Email", "البريد الإلكتروني"),
                C("Address", "Address", "العنوان"),
                C("Type", "Supplier Type", "نوع المورد"),
                C("Balance", "Amount Owed", "المبلغ المستحق"),
                C("PaymentDueDate", "Payment Due Date", "تاريخ استحقاق الدفع"),
                C("ReminderDays", "Reminder Days", "أيام التذكير"),
            },
            ["expenses"] = new[]
            {
                C("date", "Date", "التاريخ"),
                C("category", "Category", "الفئة"),
                C("deliverer", "Deliverer / Supplier", "المورّد"),
                C("liters", "Liters", "اللترات"),
                C("unit_price", "Price per Liter", "سعر اللتر"),
                C("milk_type", "Milk Type", "نوع الحليب"),
                C("shift", "Shift", "الفترة"),
                C("amount", "Amount", "المبلغ"),
                C("description", "Description", "الوصف"),
                C("paid", "Paid", "مدفوع"),
                C("recurring", "Recurring", "متكرر"),
                C("recorded_by", "Recorded By", "سجّله"),
            },
            ["sales"] = new[]
            {
                C("orderId", "Order #", "رقم الطلب"),
                C("date", "Date", "التاريخ"),
                C("customer", "Customer", "الزبون"),
                C("itemCount", "Quantity", "الكمية"),
                C("items", "Items Sold", "الأصناف المباعة"),
                C("total", "Total", "الإجمالي"),
                C("amountPaid", "Amount Paid", "المبلغ المدفوع"),
                C("balanceDue", "Balance Due", "المبلغ المتبقي"),
                C("payment", "Payment Status", "حالة الدفع"),
                C("paymentMethod", "Payment Method", "طريقة الدفع"),
                C("status", "Order Status", "حالة الطلب"),
                C("shippingAddress", "Shipping Address", "عنوان الشحن"),
                C("deliveryDate", "Delivery Date", "تاريخ التسليم"),
                C("dueDate", "Due Date", "تاريخ الاستحقاق"),
            },
            // History columns differ by the page's type; keys are the HistoryService aliases
            // lower-cased with underscores (e.g. "Order ID" -> order_id).
            ["history"] = new[]
            {
                C("order_id", "Order ID", "رقم الطلب"),
                C("id", "ID", "الرقم"),
                C("date", "Date", "التاريخ"),
                C("type", "Type", "النوع"),
                C("action", "Action", "الإجراء"),
                C("customer", "Customer", "الزبون"),
                C("supplier", "Supplier", "المورد"),
                C("item", "Item", "الصنف"),
                C("total", "Total", "الإجمالي"),
                C("amount", "Amount", "المبلغ"),
                C("status", "Status", "الحالة"),
                C("payment", "Payment", "الدفع"),
                C("items", "Items", "العناصر"),
                C("details", "Details", "التفاصيل"),
                C("user", "User", "المستخدم"),
            },
            ["report_summary"] = new[]
            {
                C("metric", "Metric", "البند"),
                C("value", "Value", "القيمة"),
            },
            ["report_trend"] = new[]
            {
                C("period", "Period", "الفترة"),
                C("sales", "Sales", "المبيعات"),
                C("expenses", "Expenses", "المصروفات"),
                C("net", "Net", "الصافي"),
            },
            ["report_products"] = new[]
            {
                C("product_name", "Product", "المنتج"),
                C("category_name", "Category", "الفئة"),
                C("unit", "Unit", "الوحدة"),
                C("sell_by_weight", "Sold by Weight", "يباع بالوزن"),
                C("quantity_sold", "Quantity Sold", "الكمية المباعة"),
                C("unit_price", "Average Price", "متوسط السعر"),
                C("total_sales", "Sales", "المبيعات"),
                C("total_cost", "Cost", "التكلفة"),
                C("profit", "Profit", "الربح"),
                C("margin", "Margin %", "هامش الربح %"),
            },
            ["report_categories"] = new[]
            {
                C("category_name", "Category", "الفئة"),
                C("quantity_sold", "Quantity Sold", "الكمية المباعة"),
                C("total_sales", "Sales", "المبيعات"),
                C("profit", "Profit", "الربح"),
                C("share", "Share of Sales %", "النسبة من المبيعات %"),
            },
            ["report_expenses"] = new[]
            {
                C("expense_date", "Date", "التاريخ"),
                C("category", "Category", "الفئة"),
                C("supplier_name", "Deliverer / Supplier", "المورّد"),
                C("liters", "Liters", "اللترات"),
                C("amount", "Amount", "المبلغ"),
                C("is_paid", "Paid", "مدفوع"),
                C("description", "Description", "الوصف"),
            },
            ["report_expcats"] = new[]
            {
                C("category", "Category", "الفئة"),
                C("expense_count", "Entries", "عدد المصاريف"),
                C("total_amount", "Total", "الإجمالي"),
                C("paid_amount", "Paid", "المدفوع"),
                C("unpaid_amount", "Unpaid", "غير المدفوع"),
            },
            ["report_milk"] = new[]
            {
                C("supplier_name", "Deliverer / Supplier", "المورّد"),
                C("delivery_count", "Deliveries", "التوريدات"),
                C("total_liters", "Total Liters", "إجمالي اللترات"),
                C("avg_unit_price", "Avg Price/L", "متوسط سعر اللتر"),
                C("total_amount", "Total Cost", "إجمالي التكلفة"),
                C("total_paid", "Paid", "المدفوع"),
                C("total_unpaid", "Unpaid Due", "المتبقي / مستحق"),
            },
            ["milk_intakes"] = new[]
            {
                C("expense_date", "Date", "التاريخ"),
                C("supplier_name", "Supplier", "المورد"),
                C("milk_type", "Milk Type", "نوع الحليب"),
                C("liters", "Liters Received", "اللترات المستلمة"),
                C("used", "Used (L)", "المستخدم (لتر)"),
                C("remaining", "Remaining (L)", "المتبقي (لتر)"),
            },
            ["milk_productions"] = new[]
            {
                C("production_date", "Date", "التاريخ"),
                C("supplier_name", "Supplier", "المورد"),
                C("part_name", "Product Made", "المنتج المصنّع"),
                C("liters_used", "Milk Used (L)", "الحليب المستخدم (لتر)"),
                C("quantity_made", "Quantity Made", "الكمية المصنّعة"),
                C("unit", "Unit", "الوحدة"),
                C("notes", "Notes", "ملاحظات"),
            },
        };

        /// <summary>Report summary metric names.</summary>
        private static readonly Dictionary<string, (string En, string Ar)> Metrics = new(StringComparer.OrdinalIgnoreCase)
        {
            ["date_from"] = ("From", "من"),
            ["date_to"] = ("To", "إلى"),
            ["total_sales"] = ("Total Sales", "إجمالي المبيعات"),
            ["total_cost"] = ("Cost of Goods Sold", "تكلفة البضاعة المباعة"),
            ["total_expenses"] = ("Total Expenses", "إجمالي المصروفات"),
            ["milk_expenses"] = ("Raw Milk Expenses", "مصروفات الحليب الخام"),
            ["general_expenses"] = ("Other Expenses", "مصروفات أخرى"),
            ["profit_before"] = ("Profit (before expenses)", "الربح (قبل المصروفات)"),
            ["profit_after"] = ("Net Profit (after expenses)", "صافي الربح (بعد المصروفات)"),
            ["order_count"] = ("Orders", "الطلبات"),
            ["items_sold"] = ("Items Sold", "الكمية المباعة"),
            ["unpaid_count"] = ("Unpaid Orders", "الطلبات غير المدفوعة"),
            ["unpaid_total"] = ("Unpaid Sales Amount", "مبلغ المبيعات غير المدفوعة"),
            ["returns_count"] = ("Returns", "المرتجعات"),
            ["returns_total"] = ("Refunded Amount", "المبالغ المستردة"),
            ["milk_liters"] = ("Raw Milk Received (L)", "الحليب الخام المستلم (لتر)"),
            ["milk_unpaid"] = ("Owed to Milk Deliverers", "المستحق لموردي الحليب"),
            ["expense_count"] = ("Expense Entries", "عدد المصاريف"),
        };

        private static readonly Dictionary<string, (string En, string Ar)> Sheets = new(StringComparer.OrdinalIgnoreCase)
        {
            ["products"] = ("Inventory", "المخزون"),
            ["customers"] = ("Customers", "الزبائن"),
            ["suppliers"] = ("Suppliers", "الموردون"),
            ["expenses"] = ("Expenses", "المصروفات"),
            ["sales"] = ("Sales", "المبيعات"),
            ["history"] = ("History", "السجل"),
            ["report_summary"] = ("Summary", "الملخص"),
            ["report_trend"] = ("Sales vs Expenses", "المبيعات مقابل المصاريف"),
            ["report_products"] = ("Products Sold", "المنتجات المباعة"),
            ["report_categories"] = ("Sales by Category", "المبيعات حسب الفئة"),
            ["report_expenses"] = ("Expenses", "المصاريف"),
            ["report_expcats"] = ("Expenses by Category", "المصاريف حسب الفئة"),
            ["report_milk"] = ("Milk Deliverers", "موردو الحليب"),
            ["milk_intakes"] = ("Raw Milk Received", "الحليب الخام المستلم"),
            ["milk_productions"] = ("What Was Made", "ما تم تصنيعه"),
        };

        /// <summary>Coded values the app stores in English, with their Arabic wording.</summary>
        private static readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Yes"] = "نعم",
            ["No"] = "لا",
            ["Paid"] = "مدفوع",
            ["Unpaid"] = "غير مدفوع",
            ["Pending"] = "معلّق",
            ["Partial"] = "مدفوع جزئياً",
            ["Completed"] = "مكتمل",
            ["Draft"] = "مسودة",
            ["Quotation"] = "عرض سعر",
            ["Returned"] = "مُرتجع",
            ["Cancelled"] = "ملغى",
            ["Cash"] = "نقداً",
            ["Credit Card"] = "بطاقة ائتمان",
            ["Card"] = "بطاقة",
            ["Pay Later"] = "دفع لاحق",
            ["Product"] = "منتج",
            ["Service"] = "خدمة",
            ["Active"] = "نشط",
            ["Inactive"] = "غير نشط",
            ["Cow Milk"] = "حليب بقر",
            ["Sheep Milk"] = "حليب غنم",
            ["Goat Milk"] = "حليب ماعز",
            ["Morning"] = "صباحي",
            ["Evening"] = "مسائي",
            ["Raw Milk"] = "حليب خام",
            ["General"] = "عام",
            ["Uncategorized"] = "غير مصنف",
            ["Walk-in"] = "زبون عابر",
            ["Cash Customer"] = "زبون نقدي",
            ["Unassigned / Walk-in"] = "غير محدد",
            ["Regular"] = "عادي",
            ["Order"] = "طلب",
            ["Payment"] = "دفعة",
            ["Sale"] = "بيع",
            ["Initial Stock"] = "مخزون أولي",
            ["ADD"] = "إضافة",
            ["EDIT"] = "تعديل",
            ["DELETE"] = "حذف",
            ["QUICK_ADD"] = "إضافة سريعة",
            ["QUICK_SUB"] = "خصم سريع",
            ["STOCK_UPDATE"] = "تحديث المخزون",
            ["IMPORT"] = "استيراد",
            ["REPORT_IMPORT"] = "استيراد تقرير",
            ["Liter"] = "لتر",
            ["L"] = "ل",
            ["g"] = "غ",
            ["Bottle"] = "زجاجة",
            ["Kg"] = "كغ",
            ["Bag"] = "كيس",
            ["Tub"] = "علبة",
            ["Piece"] = "قطعة",
            ["Jar"] = "مرطبان",
            ["Tin"] = "تنكة",
            ["Pack"] = "عبوة",
        };

        /// <summary>
        /// Arabic wordings used by more than one English value. Must stay empty: imports map
        /// Arabic back to English, which only works when each Arabic value is unique.
        /// </summary>
        public static IEnumerable<string> DuplicateArabicValues() =>
            Values.GroupBy(kv => kv.Value).Where(g => g.Count() > 1)
                  .Select(g => g.Key + " <- " + string.Join(", ", g.Select(kv => kv.Key)));

        /// <summary>Columns whose cell values are coded (translated in Arabic exports).</summary>
        private static readonly Dictionary<string, string[]> ValueColumns = new(StringComparer.OrdinalIgnoreCase)
        {
            ["products"] = new[] { "category_name", "unit_of_measure", "item_type", "is_sales_item", "is_purchase_item", "is_inactive", "is_stock_tracked", "sell_by_weight", "status" },
            ["customers"] = new[] { "CustomerType" },
            ["suppliers"] = new[] { "Type" },
            ["expenses"] = new[] { "category", "milk_type", "shift", "paid", "recurring" },
            ["sales"] = new[] { "customer", "payment", "paymentMethod", "status" },
            ["history"] = new[] { "type", "action", "customer", "status", "payment" },
            ["report_products"] = new[] { "category_name", "unit", "sell_by_weight" },
            ["report_categories"] = new[] { "category_name" },
            ["report_expenses"] = new[] { "category", "supplier_name", "is_paid" },
            ["report_expcats"] = new[] { "category" },
            ["report_milk"] = new[] { "supplier_name" },
            ["milk_intakes"] = new[] { "milk_type" },
            ["milk_productions"] = new[] { "unit" },
        };

        /// <summary>Canonical keys of the kind's coded columns (values translated on export, mapped back on import).</summary>
        public static IEnumerable<string> CodedColumns(string kind) =>
            ValueColumns.TryGetValue(kind ?? "", out var cols) ? cols : Array.Empty<string>();

        public static string Label(string kind, string key, bool ar)
        {
            if (Kinds.TryGetValue(kind ?? "", out var cols))
                foreach (var c in cols)
                    if (string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))
                        return ar ? c.Ar : c.En;
            return key;
        }

        public static string Metric(string key, bool ar) =>
            Metrics.TryGetValue(key ?? "", out var m) ? (ar ? m.Ar : m.En) : key;

        public static string Sheet(string kind, bool ar) =>
            Sheets.TryGetValue(kind ?? "", out var s) ? (ar ? s.Ar : s.En) : kind;

        /// <summary>English stored value → Arabic wording (unknown values are returned unchanged).</summary>
        public static string Value(string value, bool ar)
        {
            if (!ar || string.IsNullOrWhiteSpace(value)) return value;
            return Values.TryGetValue(value.Trim(), out var a) ? a : value;
        }

        /// <summary>
        /// Puts the table's columns in the kind's page order (unknown columns go last, empty
        /// "drop" columns are removed), translates coded values for Arabic, then renames headers.
        /// Money/number format tags on the columns are kept.
        /// </summary>
        public static DataTable Localize(DataTable dt, string kind, bool ar)
        {
            if (dt == null) return null;

            if (ar && ValueColumns.TryGetValue(kind ?? "", out var coded))
            {
                foreach (string colName in coded)
                {
                    if (!dt.Columns.Contains(colName)) continue;
                    var col = dt.Columns[colName];
                    if (col.DataType != typeof(string)) continue;
                    foreach (DataRow r in dt.Rows)
                        if (r[col] is string s) r[col] = Value(s, true);
                }
            }

            if (Kinds.TryGetValue(kind ?? "", out var cols))
            {
                int ordinal = 0;
                foreach (var c in cols)
                {
                    if (!dt.Columns.Contains(c.Key)) continue;
                    dt.Columns[c.Key].SetOrdinal(ordinal++);
                }
                foreach (var c in cols)
                {
                    if (dt.Columns.Contains(c.Key))
                        dt.Columns[c.Key].ColumnName = UniqueName(dt, ar ? c.Ar : c.En, c.Key);
                }
            }
            dt.TableName = Sheet(kind, ar);
            return dt;
        }

        private static string UniqueName(DataTable dt, string wanted, string current)
        {
            string name = wanted;
            int n = 2;
            while (dt.Columns.Contains(name) && !string.Equals(dt.Columns[name].ColumnName, current, StringComparison.OrdinalIgnoreCase))
                name = $"{wanted} ({n++})";
            return name;
        }

        /// <summary>
        /// For imports: per kind, every English/Arabic label (lower-case) → canonical key.
        /// The canonical keys themselves map to themselves, so old files keep working.
        /// </summary>
        public static Dictionary<string, Dictionary<string, string>> HeaderAliases()
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in Kinds)
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in kv.Value)
                {
                    foreach (var label in new[] { c.Key, c.En, c.Ar, LegacyArabic(c.Ar) })
                    {
                        string k = (label ?? "").Trim().ToLowerInvariant();
                        if (k.Length > 0 && !map.ContainsKey(k)) map[k] = c.Key;
                    }
                }
                result[kv.Key] = map;
            }
            return result;
        }

        /// <summary>For imports: Arabic coded value → the English value the app stores.</summary>
        public static Dictionary<string, string> ValueAliases()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in Values)
            {
                if (!map.ContainsKey(kv.Value)) map[kv.Value] = kv.Key;
                string old = LegacyArabic(kv.Value);
                if (!map.ContainsKey(old)) map[old] = kv.Key;
            }
            return map;
        }

        /// <summary>Older Arabic exports said عميل/عملاء where the app now says زبون/زبائن.</summary>
        private static string LegacyArabic(string ar) =>
            (ar ?? "").Replace("زبائن", "عملاء").Replace("زبون", "عميل");

        /// <summary>
        /// Maps an imported row's headers (English labels, Arabic labels or canonical keys) to the
        /// canonical keys of the given kind, and Arabic coded values back to the stored English.
        /// Keys that are not recognised are kept as they are.
        /// </summary>
        public static Dictionary<string, string> NormalizeRow(Dictionary<string, string> row, string kind)
        {
            if (row == null) return row;
            var aliases = HeaderAliases().TryGetValue(kind ?? "", out var m) ? m : null;
            var values = ValueAliases();
            var coded = new HashSet<string>(CodedColumns(kind), StringComparer.OrdinalIgnoreCase);
            var outRow = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in row)
            {
                string key = kv.Key ?? "";
                if (aliases != null && aliases.TryGetValue(key.Trim().ToLowerInvariant(), out var canonical))
                    key = canonical;
                string val = kv.Value;
                // Only coded columns are translated back, so free text (a product called "عام") is untouched.
                if (coded.Contains(key) && !string.IsNullOrWhiteSpace(val) && values.TryGetValue(val.Trim(), out var en))
                    val = en;
                // First non-empty value wins if two headers map to the same key.
                if (!outRow.TryGetValue(key, out var existing) || string.IsNullOrWhiteSpace(existing))
                    outRow[key] = val;
            }
            return outRow;
        }
    }
}
