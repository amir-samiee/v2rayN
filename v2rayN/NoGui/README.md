# NoGui
The main goal of this subproject is to provide a foundation that makes it easier to access GUI-level features programmatically—such as profile management, proxy configuration, and statistics—without requiring a full desktop interface.
By decoupling core functionality from the GUI, this project enables:

- Easier API development for external tools and scripts
- Support for arbitrary automation tasks (e.g., profile switching, performance testing, or configuration updates)
- A clean, service-based architecture that can be extended to support custom workflows

This implementation has currently been tested exclusively on Windows using Xray core. While the architecture is designed to be flexible, it does not yet support cross-platform operation or other core backends. Future iterations may expand this to support additional protocols or environments; any contribution is welcome.
