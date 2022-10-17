using YamlDotNet.Serialization;

Dictionary<Type, int> _TypeOrder = new()
{
    { typeof(String), 0 },
    { typeof(List<object>), 1 },
    { typeof(Dictionary<object, object>), 1}
};

var fileName = "";
var _sort = Sort.None;
var verbose = false;

var index = 0;
foreach(var arg in args)
{
    index++;
    if (arg.StartsWith("-"))
        switch (arg)
        {
            case "--file" or "-f":
                fileName = args[index + 1];
                break;

            case "--sort" or "-s":
                if (!Enum.TryParse<Sort>(args[index + 1], out _sort))
                    Console.WriteLine($"Invalid Sort. Sort must be one of {Enum.GetValues(typeof(Sort)).Cast<Sort>().Aggregate("", (current, next) => current + ", " + next)}");
                break;

            case "--verbose" or "-v":
                verbose = true;
                break;
        }
}

var yaml = new List<string>() { String.Empty };
index = 0;
using var reader = new StreamReader(fileName);

if (File.Exists(fileName))
    while (!reader.EndOfStream)
    {
        var line = reader.ReadLine();

        if(line.StartsWith("---") || line.StartsWith("..."))
        {
            index++;
            yaml.Add(String.Empty);
            continue;
        }

        yaml[index] += $"\n{line}";
    }
else
    Console.WriteLine($"Could not find file: {fileName}");

var deserializer = new DeserializerBuilder().Build();

try
{
    yaml.Select(x => deserializer.Deserialize<Dictionary<object, object>>(x))
        .ToList()
        .ForEach(x => NestedDictIteration(x));
}
catch (Exception)
{
    if (verbose)
        throw;
    else
        Console.WriteLine("Could not deserialize specified file. Make sure it is valid yaml");
}

void NestedDictIteration(Dictionary<object, object> nestedDict, int indent = -1)
{
    const char _indentChar = ' ';
    const int _indentFactor = 3;

    nestedDict = sortDict(nestedDict);

    indent++;
    foreach (var kvp in nestedDict)
    {
        string key = ((string)kvp.Key).All(Char.IsLetterOrDigit) ? (string)kvp.Key : $"\"{kvp.Key}\""; //Only put quotes around the key if it has special characters

        if (kvp.Value is string value)
        {
            if (String.IsNullOrEmpty(value))
                Console.WriteLine($"{getIndent(_indentChar, indent, _indentFactor)}{key} = \"\"");
            else if (value.All(Char.IsDigit) || value == "true" || value == "false") //Do not include quotes for numerical or boolean values (YamlDotNet interprets all numerical values as strings, so this is the best we have)
                Console.WriteLine($"{getIndent(_indentChar, indent, _indentFactor)}{key} = {value}");
            else
                Console.WriteLine($"{getIndent(_indentChar, indent, _indentFactor)}{key} = \"{value}\"");

            continue;
        }
        else if (kvp.Value is List<object> list)
        {
            if (list.Count == 0)
                Console.WriteLine($"{getIndent(_indentChar, indent, _indentFactor)}{key} = []");
            else if (list.Count == 1 && list[0] is string singleItem)
                Console.WriteLine($"{getIndent(_indentChar, indent, _indentFactor)}{key} = [ \"{singleItem}\" ]"); //Single string lists don't need to be on multiple lines
            else
            {
                Console.WriteLine($"{getIndent(_indentChar, indent, _indentFactor)}{key} = [");
                indent++;

                foreach (var item in list)
                    if (item is string str)
                        Console.WriteLine($"{getIndent(_indentChar, indent, _indentFactor)}\"{str}\"");
                    else
                    {
                        Console.WriteLine($"{getIndent(_indentChar, indent, _indentFactor)}{{");
                        NestedDictIteration((Dictionary<object, object>)item, indent);
                        Console.WriteLine($"{getIndent(_indentChar, indent, _indentFactor)}}}");
                    }

                indent--;
                Console.WriteLine($"{getIndent(_indentChar, indent, _indentFactor)}]");
            }
        }
        else if (kvp.Value is Dictionary<object, object> dictionary)
        {
            if (dictionary.Count > 0)
            {
                Console.WriteLine($"{getIndent(_indentChar, indent, _indentFactor)}{key} = {{");
                NestedDictIteration(dictionary, indent);
                Console.WriteLine($"{getIndent(_indentChar, indent, _indentFactor)}}}");
                if (indent == 0) Console.WriteLine(); //Add an extra new line at the end of top level dictionary entries
            }
            else
                Console.WriteLine($"{getIndent(_indentChar, indent, _indentFactor)}{key} = {{}}");
        }
        else
            NestedDictIteration((Dictionary<object, object>)kvp.Value, indent);
    }
    indent--;
}

static string getIndent(char indentChar, int indentCount, int indentFactor) => new(indentChar, indentCount * indentFactor);

Dictionary<object, object> sortDict(Dictionary<object, object> dict) =>
    _sort switch
    {
        Sort.Alphabetical => dict.Select(x => x)
                                 .OrderBy(x => x.Key)
                                 .ToDictionary(x => x.Key, x => x.Value),

        Sort.Type => dict.Select(kvp => new { kvp, order = _TypeOrder[kvp.Value.GetType()] })
                         .OrderBy(x => x.order)
                         .Select(x => x.kvp)
                         .ToDictionary(x => x.Key, x => x.Value),

        _ => dict,
    };

enum Sort
{
    Alphabetical,
    Type,
    None
};