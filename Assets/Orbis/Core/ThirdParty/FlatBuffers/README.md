# FlatBuffers (runtime C#)

Copia di `net/FlatBuffers/*.cs` dal repository ufficiale
[google/flatbuffers](https://github.com/google/flatbuffers), tag `v25.12.19`
(stessa versione di `flatc` usata per generare `Assets/Orbis/Core/World/Fb/`).

Licenza: Apache 2.0 (`LICENSE.txt`). Compilato in modalità sicura (senza `UNSAFE_BYTEBUFFER`),
quindi funziona su iOS, Android e web (IL2CPP) e nei test `dotnet test`.

## Modifiche locali
- `Table.cs`, `__vector_as_array<T>`: passa a `ByteBuffer.ToArray<T>` la lunghezza **in byte**
  (`len * ByteBuffer.SizeOf<T>()`) invece del numero di elementi. Senza la correzione i metodi generati
  `Get…Array()` restituiscono array troncati o lanciano eccezioni. Controllato dai test
  `TileReaderTests` (tests/Orbis.Core.Tests). Le righe modificate sono marcate `ORBIS:`.
