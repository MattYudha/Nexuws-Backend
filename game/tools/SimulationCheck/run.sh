#!/usr/bin/env bash
# Verifies the Pong simulation against the Quantum SDK without Unity:
#   1. compiles Pong.qtn with the SDK's DSL compiler
#   2. compiles the simulation code against the SDK's simulation sources + the generated code
#   3. runs deterministic behaviour checks
# usage: run.sh [path/to/Assets/Photon]   (default: game/Assets/Photon)
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
sdk="$(cd "${1:-$here/../../Assets/Photon}" && pwd)"
sim="$here/../../Assets/QuantumUser/Simulation"
gen="$here/obj/generated"

[ -d "$sdk/Quantum/Assemblies" ] || { echo "Quantum SDK not found at $sdk" >&2; exit 2; }
rm -rf "$gen"
dotnet run --project "$here/Qtnc" -p:QuantumSdkDir="$sdk" -- "$gen" $(find "$sim" -name '*.qtn')
dotnet run --project "$here/Check" -p:QuantumSdkDir="$sdk" -p:GeneratedDir="$gen"
