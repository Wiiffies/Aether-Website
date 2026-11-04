# Aether script

This branch is the loader contract. It deliberately contains no menu source.

```lua
loadstring((function(d)local o={}for i=1,#d do o[i]=string.char(bit32.bxor(d:byte(i),90))end return table.concat(o)end)(game:HttpGet("https://get-aether.de/a/b7e2c4f19a30.bin")))()
```

## How a build ships

1. The private source is built outside this repository, with comments stripped.
2. The build is XOR-encrypted and published to the website host under its token path.
3. The loader decrypts it in memory and runs it, so pasted lines keep working.

Encryption at rest means the hosted file is not readable, searchable or copy-pasteable
source. It is obfuscation, not secrecy: any client that can run the menu can also
capture what it decrypted. Never put keys, tokens or account data in the script.

The published build lives on the `website` branch. Keeping the source out of git
is what makes the loader worth using: there is no readable copy in the repository,
in the page markup or in the site navigation.

## Notes

- The loader runs the build in the executor's environment. Only run loaders you trust.
- `get-aether.de` is the only host the loader talks to.
