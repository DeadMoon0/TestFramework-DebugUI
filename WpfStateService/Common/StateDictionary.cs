using System.Collections;
using System.Diagnostics.CodeAnalysis;
using WpfStateService.StateServiceObject;

namespace WpfStateService.Common;

public class StateDictionary<TValue> : StateObject, IDictionary<string, TValue>
{
    public TValue this[string key]
    {
        get => GetValue<TValue>(key);
        set
        {
            if (!GetPropertyNames().Contains(key)) PropertyDynamic(key, value);
            SetValue(key, value);
        }
    }

    public ICollection<string> Keys => GetPropertyNames();

    public ICollection<TValue> Values => [.. GetPropertyNames().Select(GetValue<TValue>)];

    public int Count => GetPropertyNames().Count;

    public bool IsReadOnly => false;

    public void Add(string key, TValue value)
    {
        if (GetPropertyNames().Contains(key)) throw new ArgumentException("An item with the same key has already been added.");
        this[key] = value;
    }

    public void Add(KeyValuePair<string, TValue> item)
    {
        Add(item.Key, item.Value);
    }

    public void Clear()
    {
        foreach (var item in GetPropertyNames())
        {
            RemovePropertyDynamic(item);
        }
    }

    public bool Contains(KeyValuePair<string, TValue> item)
    {
        return GetPropertyNames().Contains(item.Key) && (GetValue<TValue>(item.Key)?.Equals(item.Value) ?? item.Value is null);
    }

    public bool ContainsKey(string key)
    {
        return GetPropertyNames().Contains(key);
    }

    public void CopyTo(KeyValuePair<string, TValue>[] array, int arrayIndex)
    {
        int i = arrayIndex;
        foreach (var item in this)
        {
            array[i] = item;
        }
    }

    public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator()
    {
        return GetPropertyNames().Select(x => new KeyValuePair<string, TValue>(x, GetValue<TValue>(x))).GetEnumerator();
    }

    public bool Remove(string key)
    {
        if (!ContainsKey(key)) return false;
        RemovePropertyDynamic(key);
        return true;
    }

    public bool Remove(KeyValuePair<string, TValue> item)
    {
        if (!Contains(item)) return false;
        RemovePropertyDynamic(item.Key);
        return true;
    }

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out TValue value)
    {
        value = default;
        if (!ContainsKey(key)) return false;
        value = GetValue<TValue>(key);
        return true;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}