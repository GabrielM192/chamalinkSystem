using Xunit;

// Majaribio ya ServiceRegistrationTests na ExceptionMappingTests yanategemea
// `Environment.SetEnvironmentVariable` - na hiyo ni hali ya jumla kwa process
// nzima. Chaguo-msingi la xUnit ni kukimbia test CLASSES kwa pamoja (parallel),
// kwa hiyo BadKeyFactory (inayoweka JWT key iliyovuja ili kuthibitisha kuwa
// mfumo unaikataa) ingeweza kuathiri majaribio mengine yanayokimbia wakati
// ule ule.
//
// Kwa sababu hiyo usambamba unazimwa kwa assembly hii nzima. Gharama ni ndogo
// (majaribio haya yanakimbia chini ya sekunde 1).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
