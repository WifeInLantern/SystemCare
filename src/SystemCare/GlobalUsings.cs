// Central global usings (2.19.4). The WPF SDK's ImplicitUsings set is narrower than most
// developers expect — it does not include System.Net.Http, and the effective set drifts between
// projects. Three build breaks in the 2.14–2.19 cycle came from this alone (HttpClient, File,
// Linq), each patched one namespace at a time. Declaring the common set here once ends that class
// of failure; per-file `using` stays only for namespaces that aren't universally needed.
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Threading;
global using System.Threading.Tasks;
