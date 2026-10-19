using System.CommandLine;

// =====================================================================
//  הגדרות משותפות
// =====================================================================

// מיפוי שם שפה -> סיומות קבצים
var languageExtensions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
{
    ["csharp"] = new[] { ".cs" },
    ["java"] = new[] { ".java" },
    ["python"] = new[] { ".py" },
    ["javascript"] = new[] { ".js", ".jsx" },
    ["typescript"] = new[] { ".ts", ".tsx" },
    ["html"] = new[] { ".html", ".htm" },
    ["css"] = new[] { ".css", ".scss" },
    ["sql"] = new[] { ".sql" },
    ["cpp"] = new[] { ".cpp", ".h", ".hpp", ".c" }
};

// תיקיות שלא נכללות ב-bundle
var excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "bin", "obj", "debug", "release", "node_modules", ".git", ".vs", ".idea", "publish"
};

// מחזיר את השפות הלא מוכרות מתוך רשימה (משמש גם ב-bundle וגם ב-create-rsp)
List<string> FindInvalidLanguages(IEnumerable<string> values) =>
    values.Where(v => !v.Equals("all", StringComparison.OrdinalIgnoreCase)
                      && !languageExtensions.ContainsKey(v))
          .ToList();

string AllowedLanguagesText() => "all, " + string.Join(", ", languageExtensions.Keys);

// =====================================================================
//  פקודת bundle
// =====================================================================

var languageOption = new Option<string[]>("--language", "-l")
{
    Description = "Programming languages to include (or 'all')",
    Required = true,
    AllowMultipleArgumentsPerToken = true
};
languageOption.Validators.Add(result =>
{
    var values = result.GetValueOrDefault<string[]>() ?? Array.Empty<string>();
    if (values.Length == 0)
    {
        result.AddError("At least one language is required.");
        return;
    }
    var invalid = FindInvalidLanguages(values);
    if (invalid.Count > 0)
        result.AddError($"Unknown language(s): {string.Join(", ", invalid)}. Allowed: {AllowedLanguagesText()}");
});

var outputOption = new Option<FileInfo>("--output", "-o")
{
    Description = "Bundle file name or full path (default: bundle.txt)"
};

var noteOption = new Option<bool>("--note", "-n")
{
    Description = "Write the source file name and relative path as a comment"
};

var sortOption = new Option<string>("--sort", "-s")
{
    Description = "Sort files by 'name' (default) or 'type'",
    DefaultValueFactory = _ => "name"
};
sortOption.AcceptOnlyFromAmong("name", "type");

var removeEmptyLinesOption = new Option<bool>("--remove-empty-lines", "-r")
{
    Description = "Remove empty lines from the code"
};

var authorOption = new Option<string>("--author", "-a")
{
    Description = "Author name to write at the top of the bundle"
};

var bundleCommand = new Command("bundle", "Bundle code files into a single file");
bundleCommand.Options.Add(languageOption);
bundleCommand.Options.Add(outputOption);
bundleCommand.Options.Add(noteOption);
bundleCommand.Options.Add(sortOption);
bundleCommand.Options.Add(removeEmptyLinesOption);
bundleCommand.Options.Add(authorOption);

bundleCommand.SetAction(parseResult =>
{
    var languages = parseResult.GetValue(languageOption)!;
    var output = parseResult.GetValue(outputOption) ?? new FileInfo("bundle.txt");
    var note = parseResult.GetValue(noteOption);
    var sort = parseResult.GetValue(sortOption) ?? "name";
    var removeEmptyLines = parseResult.GetValue(removeEmptyLinesOption);
    var author = parseResult.GetValue(authorOption);

    var sourceDir = Directory.GetCurrentDirectory();

    // 1. אילו סיומות נכללות
    var includeAll = languages.Any(l => l.Equals("all", StringComparison.OrdinalIgnoreCase));
    var extensions = (includeAll
            ? languageExtensions.Values.SelectMany(e => e)
            : languages.SelectMany(l => languageExtensions[l]))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // 2. איסוף קבצים: לפי סיומת, בלי תיקיות מוחרגות ובלי קובץ הפלט עצמו
    IEnumerable<string> files = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories)
        .Where(f => extensions.Contains(Path.GetExtension(f)))
        .Where(f => !Path.GetRelativePath(sourceDir, f)
                         .Split(Path.DirectorySeparatorChar)
                         .Any(part => excludedFolders.Contains(part)))
        .Where(f => !string.Equals(Path.GetFullPath(f), output.FullName, StringComparison.OrdinalIgnoreCase));

    // 3. מיון
    files = sort.Equals("type", StringComparison.OrdinalIgnoreCase)
        ? files.OrderBy(f => Path.GetExtension(f)).ThenBy(f => Path.GetFileName(f))
        : files.OrderBy(f => Path.GetFileName(f));

    var fileList = files.ToList();
    if (fileList.Count == 0)
    {
        Console.WriteLine("No matching code files were found in this folder.");
        return 1;
    }

    // 4. כתיבה לקובץ ה-bundle
    try
    {
        using var writer = new StreamWriter(output.FullName);

        if (!string.IsNullOrWhiteSpace(author))
        {
            writer.WriteLine($"// Author: {author}");
            writer.WriteLine();
        }

        foreach (var file in fileList)
        {
            if (note)
                writer.WriteLine($"// Source: {Path.GetFileName(file)} ({Path.GetRelativePath(sourceDir, file)})");

            var lines = File.ReadAllLines(file);
            if (removeEmptyLines)
                lines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

            foreach (var line in lines)
                writer.WriteLine(line);

            writer.WriteLine();
        }

        Console.WriteLine($"Bundle created: {output.FullName} ({fileList.Count} files)");
        return 0;
    }
    catch (DirectoryNotFoundException)
    {
        Console.WriteLine($"Error: the folder for '{output.FullName}' does not exist.");
    }
    catch (UnauthorizedAccessException)
    {
        Console.WriteLine($"Error: no permission to write to '{output.FullName}'.");
    }
    catch (IOException ex)
    {
        Console.WriteLine($"Error writing the bundle file: {ex.Message}");
    }
    return 1;
});

// =====================================================================
//  פקודת create-rsp
// =====================================================================

var rspFileOption = new Option<string>("--file", "-f")
{
    Description = "Response file name (default: bundle.rsp)",
    DefaultValueFactory = _ => "bundle.rsp"
};

var createRspCommand = new Command("create-rsp", "Create a response file with a ready bundle command");
createRspCommand.Options.Add(rspFileOption);

createRspCommand.SetAction(parseResult =>
{
    var rspFile = parseResult.GetValue(rspFileOption) ?? "bundle.rsp";
    if (!rspFile.EndsWith(".rsp", StringComparison.OrdinalIgnoreCase))
        rspFile += ".rsp";

    // ---- language (חובה, עם ולידציה וחזרה עד לקלט תקין) ----
    string[] languages;
    while (true)
    {
        Console.Write($"Languages (separated by spaces) [{AllowedLanguagesText()}]: ");
        languages = (Console.ReadLine() ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (languages.Length == 0)
        {
            Console.WriteLine("  Language is required.");
            continue;
        }
        var invalid = FindInvalidLanguages(languages);
        if (invalid.Count > 0)
        {
            Console.WriteLine($"  Unknown language(s): {string.Join(", ", invalid)}");
            continue;
        }
        break;
    }

    // ---- output ----
    Console.Write("Output file name or path (Enter for bundle.txt): ");
    var output = Console.ReadLine()?.Trim().Trim('"');

    // ---- note ----
    var note = AskYesNo("Add source file path as a comment? (y/n): ");

    // ---- sort ----
    string sort;
    while (true)
    {
        Console.Write("Sort by 'name' or 'type' (Enter for name): ");
        sort = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
        if (sort == "") sort = "name";
        if (sort is "name" or "type") break;
        Console.WriteLine("  Please type 'name' or 'type'.");
    }

    // ---- remove-empty-lines ----
    var removeEmptyLines = AskYesNo("Remove empty lines? (y/n): ");

    // ---- author ----
    Console.Write("Author name (Enter to skip): ");
    var author = Console.ReadLine()?.Trim();

    // ---- בניית הפקודה ----
    var parts = new List<string> { "bundle", "--language" };
    parts.AddRange(languages);

    if (!string.IsNullOrWhiteSpace(output))
        parts.Add($"--output {Quote(output)}");
    if (note)
        parts.Add("--note");
    parts.Add($"--sort {sort}");
    if (removeEmptyLines)
        parts.Add("--remove-empty-lines");
    if (!string.IsNullOrWhiteSpace(author))
        parts.Add($"--author {Quote(author)}");

    var command = string.Join(" ", parts);

    try
    {
        File.WriteAllText(rspFile, command);
        Console.WriteLine();
        Console.WriteLine($"Response file created: {Path.GetFullPath(rspFile)}");
        Console.WriteLine($"Command: {command}");
        Console.WriteLine($"Run it with: to @{rspFile}");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.WriteLine($"Error creating the response file: {ex.Message}");
        return 1;
    }
});

// עוטף במרכאות ערך שיש בו רווח (למשל נתיב עם תיקייה בשתי מילים)
static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

// שאלת כן/לא עם חזרה עד לתשובה תקינה
static bool AskYesNo(string question)
{
    while (true)
    {
        Console.Write(question);
        var answer = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
        if (answer is "y" or "yes") return true;
        if (answer is "n" or "no" or "") return false;
        Console.WriteLine("  Please type y or n.");
    }
}

// =====================================================================
//  פקודת השורש
// =====================================================================

var rootCommand = new RootCommand("CLI for bundling code files into a single file");
rootCommand.Add(bundleCommand);
rootCommand.Add(createRspCommand);

return await rootCommand.Parse(args).InvokeAsync();