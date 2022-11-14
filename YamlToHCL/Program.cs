using System.Text;
using YamlDotNet.Serialization;

Dictionary<Type, int> _TypeOrder = new()
{
    { typeof(String), 0 },
    { typeof(List<object>), 1 },
    { typeof(Dictionary<object, object>), 1}
};

var files = new List<string>();
var sort = Sort.None;
var verbose = false;
var terraform = Terraform.None;
var manifestCount = 0;
var fileOut = false;
var removeEmpty = false;
FileStream currentFile = null;

const char _indentChar = ' ';
const int _indentFactor = 3;

var index = 0;
foreach(var arg in args)
{
    if (arg.StartsWith("-"))
        switch (arg)
        {
            case "--file" or "-f" or "--folder":
                var input = args[index + 1];
                if (input.EndsWith("/") || input.EndsWith(@"\"))
                {
                    files = Directory.GetFiles(input)
                                     .Where(x => x.EndsWith(".yaml") || x.EndsWith(".yml"))
                                     .ToList();
                }
                else files = new List<string>() { input };
                break;

            case "--sort" or "-s":
                if (!Enum.TryParse<Sort>(args[index + 1], out sort))
                    Console.WriteLine($"Invalid Sort. Sort must be one of {Enum.GetValues(typeof(Sort)).Cast<Sort>().Aggregate("", (current, next) => current + ", " + next)}");
                break;

            case "--verbose" or "-v":
                verbose = true;
                break;

            case "--terraform" or "-t":
                if (!Enum.TryParse<Terraform>(args[index + 1], out terraform))
                    Console.WriteLine($"Invalid Terraform. Terraform must be one of {Enum.GetValues(typeof(Terraform)).Cast<Sort>().Aggregate("", (current, next) => current + ", " + next)}");
                break;

            case "--file-out" or "-fo":
                fileOut = true;
                break;

            case "--remove-empty" or "-re":
                removeEmpty = true;
                break;
        }
    index++;
}
var yaml = new List<string>() { String.Empty };

foreach (var file in files)
{
    if (fileOut) currentFile = File.OpenWrite(Path.ChangeExtension(file, ".tf"));

    if (File.Exists(file))
    {
        index = 0;
        using var reader = new StreamReader(file);

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();

            if (line.StartsWith("---") || line.StartsWith("..."))
            {
                index++;
                yaml.Add(String.Empty);
                continue;
            }

            yaml[index] += $"\n{line}";
        }
    }
    else
        Console.WriteLine($"Could not find file: {file}");

    var deserializer = new DeserializerBuilder().Build();

    try
    {
        var dicts = yaml.Select(x => deserializer.Deserialize<Dictionary<object, object>>(x));

        foreach (var dict in dicts)
        {
            switch (terraform)
            {
                case Terraform.None:
                    NestedDictIteration(dict);
                    break;

                case Terraform.kubernetes_manifest:
                    var resource = new Dictionary<object, object>()
                    {
                        { "manifest", dict }
                    };

                    Output($"resource \"kubernetes_manifest\" \"manifest_{manifestCount}\" {{");
                    NestedDictIteration(resource, 0);
                    Output("}");
                    Output();
                    manifestCount++;
                    break;

                case Terraform.kubectl_manifest:
                    Output($"resource \"kubectl_manifest\" \"manifest_{manifestCount}\" {{");
                    Output($"{getPrefix(_indentChar, 1, _indentFactor)}yaml_body = yamlencode(");
                    NestedDictIteration(dict, 1);
                    Output($"{getPrefix(_indentChar, 1, _indentFactor)})");
                    Output("}");
                    Output();
                    manifestCount++;
                    break;
            }
        }

        if (currentFile is not null) currentFile.Close();
    }
    catch (Exception)
    {
        if (verbose) throw;
        else Console.WriteLine($"Could not deserialize {file}. Make sure it is valid yaml");
    }
}

void NestedDictIteration(Dictionary<object, object> nestedDict, int indent = -1)
{
    nestedDict = sortDict(nestedDict);

    indent++;
    foreach (var kvp in nestedDict)
    {
        string key = ((string)kvp.Key).All(Char.IsLetterOrDigit) ? (string)kvp.Key : $"\"{kvp.Key}\""; //Only put quotes around the key if it has special characters

        if (kvp.Value is string value)
        {
            if (String.IsNullOrEmpty(value) && removeEmpty) continue;

            if (String.IsNullOrEmpty(value))
                Output($"{getPrefix(_indentChar, indent, _indentFactor)}{key} = \"\"");
            else if (value.All(Char.IsDigit) || value == "true" || value == "false") //Do not include quotes for numerical or boolean values (YamlDotNet interprets all numerical values as strings, so this is the best we have)
                Output($"{getPrefix(_indentChar, indent, _indentFactor)}{key} = {value}");
            else
                Output($"{getPrefix(_indentChar, indent, _indentFactor)}{key} = \"{value}\"");

            continue;
        }
        else if (kvp.Value is List<object> list)
        {
            if (list.Count == 0 && removeEmpty) continue;

            if (list.Count == 0)
                Output($"{getPrefix(_indentChar, indent, _indentFactor)}{key} = []");
            else if (list.Count == 1 && list[0] is string singleItem)
                Output($"{getPrefix(_indentChar, indent, _indentFactor)}{key} = [ \"{singleItem}\" ]"); //Single string lists don't need to be on multiple lines
            else
            {
                Output($"{getPrefix(_indentChar, indent, _indentFactor)}{key} = [");
                indent++;

                var last = list.Last();
                foreach (var item in list)
                    if (item is string str)
                        Output($"{getPrefix(_indentChar, indent, _indentFactor)}\"{str}\"{ (String.Equals(str, last) ? "" : ",") }");
                    else
                    {
                        Output($"{getPrefix(_indentChar, indent, _indentFactor)}{{");
                        NestedDictIteration((Dictionary<object, object>)item, indent);
                        Output($"{getPrefix(_indentChar, indent, _indentFactor)}}}{ (item == last ? "" : ",") }");
                    }

                indent--;
                Output($"{getPrefix(_indentChar, indent, _indentFactor)}]");
            }
        }
        else if (kvp.Value is Dictionary<object, object> dictionary)
        {
            if (dictionary.Count == 0 && removeEmpty) continue; 

            if (dictionary.Count > 0)
            {
                Output($"{getPrefix(_indentChar, indent, _indentFactor)}{key} = {{");
                NestedDictIteration(dictionary, indent);
                Output($"{getPrefix(_indentChar, indent, _indentFactor)}}}");
                if (indent == 0) Output(); //Add an extra new line at the end of top level dictionary entries
            }
            else
                Output($"{getPrefix(_indentChar, indent, _indentFactor)}{key} = {{}}");
        }
        else if (kvp.Value is null)
        {
            if (!removeEmpty) Output($"{getPrefix(_indentChar, indent, _indentFactor)}{key} = null");
        }
        else
            NestedDictIteration((Dictionary<object, object>)kvp.Value, indent);
    }
    indent--;
}

void Output(string line = "")
{
    if (fileOut) currentFile.Write(Encoding.UTF8.GetBytes($"{line}\r\n"));
    else Console.WriteLine(line);
}

static string getPrefix(char indentChar, int indentCount, int indentFactor) => new(indentChar, indentCount * indentFactor);

Dictionary<object, object> sortDict(Dictionary<object, object> dict) =>
    sort switch
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

enum Terraform
{
    kubernetes_manifest,
    kubectl_manifest,
    None
}