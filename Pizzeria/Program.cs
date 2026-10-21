using System.Globalization;

public interface IDiscountable { decimal ApplyDiscount(decimal percent); string GetDiscountInfo(); }

public abstract class MenuItem {
    private readonly string _name;
    protected decimal _basePrice;
    public string Name => _name;
    protected MenuItem(string name, decimal basePrice) { if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Название не может быть пустым.", nameof(name)); if (basePrice < 0) throw new ArgumentException("Базовая цена не может быть отрицательной.", nameof(basePrice)); _name = name; _basePrice = basePrice; }
    public abstract decimal GetFinalPrice(); public abstract string GetInfo(); public virtual string GetCategory() => "Позиция меню";
}

public sealed class Pizza : MenuItem, IDiscountable {
    public int ToppingsCount { get; }
    public Pizza(string name, decimal basePrice, int toppingsCount) : base(name, basePrice) { if (toppingsCount < 0) throw new ArgumentException("Число топпингов не может быть отрицательным.", nameof(toppingsCount)); ToppingsCount = toppingsCount; }
    public override decimal GetFinalPrice() => _basePrice + ToppingsCount * 50m;
    public override string GetInfo() => $"Пицца «{Name}», топпингов: {ToppingsCount}, итого: {GetFinalPrice():0.00} руб.";
    public override string GetCategory() => "Пицца";
    public decimal ApplyDiscount(decimal percent) => GetFinalPrice() * (1 - percent / 100m) * (ToppingsCount > 3 ? 0.95m : 1m);
    public string GetDiscountInfo() => ToppingsCount > 3 ? "Скидка 5% за более чем 3 топпинга" : "Дополнительная скидка за топпинги не применяется";
}

public sealed class Drink : MenuItem {
    public int VolumeMl { get; }
    public Drink(string name, decimal basePrice, int volumeMl) : base(name, basePrice) { if (volumeMl < 0) throw new ArgumentException("Объём не может быть отрицательным.", nameof(volumeMl)); VolumeMl = volumeMl; }
    public override decimal GetFinalPrice() => _basePrice + VolumeMl * 0.5m;
    public override string GetInfo() => $"Напиток «{Name}», {VolumeMl} мл, итого: {GetFinalPrice():0.00} руб.";
    public override string GetCategory() => "Напиток";
}

public sealed class Dessert : MenuItem, IDiscountable {
    public bool IsTodayFresh { get; }
    public Dessert(string name, decimal basePrice, bool isTodayFresh) : base(name, basePrice) => IsTodayFresh = isTodayFresh;
    public override decimal GetFinalPrice() => IsTodayFresh ? _basePrice : _basePrice * 0.7m;
    public override string GetInfo() => $"Десерт «{Name}», {(IsTodayFresh ? "свежий" : "не свежий")}, итого: {GetFinalPrice():0.00} руб.";
    public override string GetCategory() => "Десерт";
    public decimal ApplyDiscount(decimal percent) => GetFinalPrice() * (1 - percent / 100m) * (IsTodayFresh ? 1m : 0.9m);
    public string GetDiscountInfo() => IsTodayFresh ? "Скидка за распродажу не применяется" : "Скидка 10% за несвежий десерт";
}

public sealed class Combo : MenuItem {
    private readonly IReadOnlyList<MenuItem> _items;
    public Combo(string name, IEnumerable<MenuItem> items) : base(name, CalculateBasePrice(items)) { var list = items.ToList(); if (list.Count == 0 || list.Any(x => x is null)) throw new ArgumentException("Комбо должно содержать позиции.", nameof(items)); _items = list.AsReadOnly(); }
    private static decimal CalculateBasePrice(IEnumerable<MenuItem> items) => items?.Sum(x => x.GetFinalPrice()) ?? throw new ArgumentNullException(nameof(items));
    public override decimal GetFinalPrice() => _items.Sum(x => x.GetFinalPrice()) * 0.85m;
    public override string GetInfo() => $"Комбо «{Name}», позиций: {_items.Count}, скидка 15%, итого: {GetFinalPrice():0.00} руб.";
    public override string GetCategory() => "Комбо";
}

public sealed class Order {
    private readonly List<MenuItem> _items = new(); private const int MaxItems = 20;
    public void Add(MenuItem item) { ArgumentNullException.ThrowIfNull(item); if (_items.Count >= MaxItems) throw new InvalidOperationException($"В заказе не может быть больше {MaxItems} позиций."); _items.Add(item); }
    public IReadOnlyList<MenuItem> GetItems() => _items.AsReadOnly();
    public decimal GetTotal() => _items.Sum(x => x.GetFinalPrice());
    public void PrintReceipt() { Console.WriteLine("=== ЧЕК ==="); foreach (var item in _items) Console.WriteLine($"[{item.GetCategory()}] {item.GetInfo()}"); Console.WriteLine("------------------------------"); Console.WriteLine($"Итого: {GetTotal():0.00} руб."); }
    public decimal GetTotalWithDiscount(decimal percent) { if (percent < 0 || percent > 100) throw new ArgumentOutOfRangeException(nameof(percent)); return _items.Sum(item => item is IDiscountable d ? d.ApplyDiscount(percent) : item.GetFinalPrice() * (1 - percent / 100m)); }
    public IReadOnlyList<MenuItem> GetItemsSortedByPrice() => _items.OrderBy(x => x.GetFinalPrice()).ToList().AsReadOnly();
    public MenuItem? FindMostExpensive() => _items.MaxBy(x => x.GetFinalPrice());
}

internal static class Program {
    private static void Main() {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; var order = new Order();
        order.Add(new Pizza("Маргарита", 400m, 3)); order.Add(new Drink("Кола", 50m, 500)); order.Add(new Dessert("Тирамису", 320m, true)); order.Add(new Pizza("Пепперони", 300m, 2)); order.Add(new Dessert("Чизкейк", 280m, false)); order.Add(new Drink("Лимонад", 70m, 330)); order.Add(new Combo("Обеденное комбо", new MenuItem[] { new Pizza("Мини-пицца", 250m, 1), new Drink("Сок", 40m, 200) }));
        order.PrintReceipt(); Console.WriteLine($"Скидка 10%: {order.GetTotalWithDiscount(10):0.00} руб."); var expensive = order.FindMostExpensive(); Console.WriteLine($"Самая дорогая позиция: {expensive?.Name} ({expensive?.GetFinalPrice():0.00} руб.)");
        Console.WriteLine("\nПозиции по возрастанию цены:"); foreach (var item in order.GetItemsSortedByPrice()) Console.WriteLine($"- {item.Name}: {item.GetFinalPrice():0.00} руб.");
        var readOnlyItems = order.GetItems(); // readOnlyItems.Add(...) не компилируется: IReadOnlyList не имеет Add.
        Console.WriteLine($"\nСписок возвращён только для чтения: {readOnlyItems.Count} позиций.");
    }
}
