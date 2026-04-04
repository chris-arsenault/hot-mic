.PHONY: ci build test

ci: build test

build:
	dotnet restore tests/HotMic.Core.Tests/HotMic.Core.Tests.csproj
	dotnet build src/HotMic.Common/HotMic.Common.csproj -c Release --no-restore
	dotnet build src/HotMic.Core/HotMic.Core.csproj -c Release --no-restore
	dotnet build tests/HotMic.Core.Tests/HotMic.Core.Tests.csproj -c Release --no-restore

test:
	dotnet test tests/HotMic.Core.Tests/HotMic.Core.Tests.csproj -c Release --no-build --verbosity normal
