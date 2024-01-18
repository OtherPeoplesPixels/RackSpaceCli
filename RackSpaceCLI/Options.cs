using CommandLine;

namespace RackSpaceCLI;

public class Options
{
    [Option('f', "file", Required = false, HelpText = "Path to the CSV file containing domains")]
    public string FilePath { get; set; }

    [Option('d', "domain", Required = false, HelpText = "Single domain to process")]
    public string Domain { get; set; }

    [Option('m', "mail", Required = false, HelpText = "Single mailbox to process")]
    public string Mailbox { get; set; }

    // Add help section
}