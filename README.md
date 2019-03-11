## Installation instructions
- Use a machine with Visual Studio or Monodevelop installed. OCDT_Notifier is written in C#.
- Make sure that NuGet is installed. You will need to `nuget restore` to get the packages.
  - Add the **OCDT C# SDK** repository to NuGet, using the URL `https://ocdt.esa.int/nexus/service/local/nuget/ocdt-releases/`. You may need to enter your password.
- Get the [MessageServices](https://gitlab.ocdt.esa.int/ocdt/ui/tree/development/MessageServices) directory from the [OCDT/ui](https://gitlab.ocdt.esa.int/ocdt/ui/tree/development) repository.
- Store a `config.yml` file alongside your executable. An example file is provided in the root folder.