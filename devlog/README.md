# DevLog

Folder for development logs and design notes of the project. For the roadmap, TODOs, and ideas, see [`PLANNING.md`](../PLANNING.md).

## Structure

```text
devlog/
├── README.md              <- this file, do not edit
├── devlog.md              <- current active log
└── devlog-YYYY-MM-DD.md   <- archived logs (the date indicates when it was archived)
```

There is always exactly one active file: `devlog.md`. When this file reaches or exceeds 1000 lines, it is archived by renaming it, and a new empty one is created (using the template provided below).

## Writing and Archiving Rules

- **Always write in the current file:** `devlog.md`. If it doesn't exist, create it using the template below.
- **Reverse Chronological Order:** The latest entry must **always be added at the top** of the file, right below the main title.
- **Archiving Procedure (Over 1000 lines):**
  1. Rename the current `devlog.md` to `devlog-YYYY-MM-DD.md` using the current day's date.
  2. Do not edit the newly archived file anymore.
  3. Immediately create a new `devlog.md` file inserting the base template provided below.
  4. Write your new log entry in the new file.

## Template for a new devlog.md

Copy the text below when creating a new `devlog.md` file:

```markdown
# DEVLOG 3D-Ray

Development diary of the project. The newest entries go at the top.
Each entry notes what was done, decisions made, problems encountered, and what comes next.

Suggested format for each entry:

## YYYY-MM-DD - Short title
**Done:** what was completed  
**Decisions:** technical/design choices and why  
**Issues:** what got stuck and how (or if) it was resolved  
**Next:** the next step  

---
```
