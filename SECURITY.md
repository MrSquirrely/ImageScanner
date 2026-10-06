# Security Policy

## Supported Versions

This project is built using C# and .NET, specifically designed as a Windows Presentation Foundation (WPF) desktop application. Security updates, patches, and runtime OS version checks are exclusively maintained for the following supported operating systems and frameworks.

| OS / Framework | Version | Supported |
| :--- | :--- | :--- |
| **Windows 11** | 21H2 and newer | ✅ Yes |
| **Windows 10** | 21H2 and newer | ✅ Yes |
| **Windows 8.1 / 8 / 7** | All versions | ❌ No |
| **macOS / Linux** | All versions | ❌ No |
| **.NET Framework** | .NET 10.0 | ✅ Yes |
| **.NET Framework** | .NET 9.0 and older | ❌ No |

*Note: Execution on unsupported operating systems is not tested and inherently unsupported. Any security vulnerabilities arising from bypassing OS checks to run the application in those environments will not be addressed.*

## Reporting a Vulnerability

If you discover a security vulnerability, please practice responsible disclosure. **Do not open a public GitHub issue.** 

To report a security issue, please email the project maintainer directly. Include the following details in your report:
* A description of the vulnerability and its potential impact.
* Detailed steps to reproduce the issue.
* The specific version of Windows (10 or 11) used during testing.
* Any known mitigations or suggested fixes.

You should receive an acknowledgment of your report within 48 hours, followed by an estimated timeline for a patch or mitigation. 

## WPF & C# Security Guidelines for Contributors

To maintain the security integrity of this application, all pull requests and code contributions must adhere to the following C# and WPF specific security standards.

### 1. Data Protection & Cryptography
* **DPAPI Usage:** Never store plain-text passwords, credentials, or sensitive configuration data. Always use the Windows Data Protection API (DPAPI) via `System.Security.Cryptography.ProtectedData` configured with `DataProtectionScope.CurrentUser` to encrypt sensitive data at rest locally.
* **Modern Cryptography:** Avoid deprecated hashing algorithms. Use SHA-256 or higher for hashing, and AES-GCM for symmetric encryption if DPAPI is not applicable.

### 2. File System & Image Parsing
* **Path Traversal Protection:** When processing local files, enumerating directories, or generating thumbnails, sanitize all file paths. Ensure constructed paths are strictly validated against directory traversal attacks (e.g., stripping `../` or explicitly validating root directories using `Path.GetFullPath()`).
* **Safe Media Processing:** Rely on trusted, up-to-date processing libraries (e.g., `SixLabors.ImageSharp`, `Magick.NET`) rather than executing arbitrary system codecs. Wrap all image metadata/EXIF extraction in strict `try-catch` blocks to prevent malformed image headers from causing denial-of-service (DoS) crashes.

### 3. Native Interop & Win32 API (P/Invoke)
* **Safe Handles:** When interacting with the Win32 API for OS-level integrations (such as single-instance Mutex enforcement, window message broadcasting, or DWM Mica/Acrylic backdrop rendering), always use `SafeHandle` to prevent resource leaks and unauthorized memory access.
* **Input Validation for Native Calls:** Strictly validate all data passed to unmanaged code. P/Invoke boundaries bypass standard .NET memory safety and can be exploited via buffer overflows if untrusted data is passed to a native Windows DLL.

### 4. Application State & UI Security
* **XAML Security:** Avoid using `XamlReader.Parse()` with untrusted or user-supplied strings, as this can lead to XAML injection attacks and arbitrary object instantiation.
* **Memory Safety:** The `unsafe` keyword in C# is strictly prohibited in this codebase unless accompanied by a documented, reviewed, and unavoidable performance justification. Rely on `Span<T>` and `Memory<T>` for safe, high-performance memory manipulation.
