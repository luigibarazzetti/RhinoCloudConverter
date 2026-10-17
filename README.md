# RhinoCloud Converter - fresh GitHub project

Windows graphical application to convert multiple E57, LAS and LAZ files to COPC (.copc.laz), using PDAL and the optional E57 driver.

## Starting from an empty GitHub repository (no Visual Studio or Python)

1. Open https://github.com/new and create a repository, e.g. `RhinoCloudConverter`, with **Add a README file** enabled (this creates the `main` branch). Alternatively use an existing empty repository with a `main` branch.
2. Extract `RhinoCloudConverter_FRESH.zip` on your PC. Do not upload the ZIP as a single file.
3. In the GitHub repository use **Add file > Upload files** to upload `README.md`, `src/Program.cs` and `src/RhinoCloudConverter.csproj` (GitHub's browser uploader may not preserve directory paths reliably if selecting files individually: drag the `src` folder itself, or create files using the GitHub editor with their full paths).
4. Create `.github/workflows/build-portable.yml` using **Add file > Create new file** and paste the contents of the corresponding file from the ZIP. Commit to `main`.
5. Go to **Actions > Build RhinoCloud Converter Windows > Run workflow**. The push that adds the workflow may start the build automatically.
6. After a successful run, open its **Artifacts** section and download `RhinoCloudConverter-Windows-x64-Portable`.
7. Extract the downloaded artifact, then extract the contained portable ZIP. Run `RhinoCloudConverter.exe` on Windows, keeping `engine/` next to it.

## Converter settings

- Input: `.e57`, `.las`, `.laz`, single or multiple files.
- Output: combined COPC or individual COPC files.
- Default coordinate behavior: no explicit unit conversion and no manual scale override. COPC/LAS quantizes coordinates, so the output is not necessarily bit-for-bit identical to the input.
- Optional custom scale: in the source coordinate units (e.g. 0.001 means 1 mm **only if the source uses metres**).
- Input scans must already be registered/aligned. This tool does not align scans or infer CRS.

## Important limitations

- This repository contains SOURCE CODE and an automated build workflow, not precompiled PDAL libraries. GitHub Actions downloads and bundles dependencies.
- The workflow and the converter have **not been run end-to-end on a Windows runner in this environment**. Driver availability and package compatibility are checked during the build; a successful build is not proof that every E57 variant will convert.
- Bundling the entire conda environment may make the archive very large. If GitHub Actions fails, inspect the first failing step.
- Very large merged clouds can require substantial temporary disk space and RAM during COPC writing.
