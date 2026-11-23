# Getting started

A MirrorPulse Adapter runs as a Worker process. The public SDK provides the versioned protocol types and helps the Worker communicate with the MirrorPulse Host. One deployment describes one storage root.

## Build the sample and run the contract checks

From the SDK repository root, use the .NET SDK version in `global.json`.

```powershell
dotnet restore MirrorPulse.Adapter.sln --locked-mode
dotnet build MirrorPulse.Adapter.sln -c Release --no-restore
dotnet run --project tests/MirrorPulse.Adapter.ContractTests -c Release --no-build
```

The contract test executable exercises the wire contracts and launches the sample memory Worker. The memory Worker demonstrates the process boundary. A production Worker must declare only the capabilities its storage backend supports.

Use the [Adapter template](https://github.com/mirrorpulse/adapter-template) when starting an independent Adapter. Keep runtime data and credentials under the Host's ownership. Build and sign a package with the template's release tooling before testing installation in MirrorPulse.
