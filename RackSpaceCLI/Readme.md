## RackSpace CLI

### Intro

### Usage

### Rate Limits

#### Notes

Currently:
- Looks up all domains in the csv file
- Checks with RackSpace, if the domain is found and has an email, the emails are stored to be deleted in the next step. 
The domain is also stored in a csv file
- If the domain is found but no email is found, the domain is stored in a csv for deletion.
- If the domain is not found, it is placed in the invalid_domains file for tracking.

Todo:
- Possibility create a method that checks csv files Domains_not_found and any csv file for processing to see if any of
the domains overlap
- Create the next steps to delete the domains that are in the _mailboxes/_valid_domains or store the domains in a csv file for later