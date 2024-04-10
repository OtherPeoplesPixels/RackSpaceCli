## RackSpace CLI


### Usage

Make sure you have the latest version of the DotNet runtime installed on your system. You can download the runtime at [this link](https://dotnet.microsoft.com/en-us/download).

Once the runtime is installed, place the RackSpaceCLI folder in an accessable location like the Desktop or Home folder. The applications folder has all the files it needs to run.
There is no installation step, once the files are on your system you can start running the application.

To run, note the location of the CSV file you want to process or place it in the RackSpaceCLI folder.
Next, open a terminal window and navigate to the RackspaceCLI directory. Ensure you have execute access by running:
```bash
$ sudo chmod +x RackspaceCLI
```
You should now beable to run the application. To process you CSV file use:
```bash
$ ./RackspaceCLI -f /path/to/csv/file.csv
```
The application will begin to process the CSV and will create a new directory inside the RackSpaceCLI folder called Csv_files.
Inside Csv_files the application creates two new CSV files, Domains_not_found.csv and Domains_without_email.csv.
These files keep track of domains that do not have emails or domains that are not found in the RackSpace API.
As you process CSV files,  any duplicate domains that were previously processed will be stored in the Domains_without_email.csv file in the subsequent run.

When the application finishes the email lookup you will be prompted to delete the mailboxes it found in the search. Once the mailbox deletion step is finished you will then be prompted to delete the associated domains.
You have the options to skip any deletion step after processing the csv.
