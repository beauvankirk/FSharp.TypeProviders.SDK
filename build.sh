#!/bin/bash

find . -name "global.json"

dotnet tool restore
dotnet paket restore
dotnet fake run build.fsx $@
