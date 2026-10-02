Dev reference only. The game builds the Textured world itself (Assets/Scripts/Host/TexturedBuilder.cs) and the camp
pictures (TexturedBuilder.BuildCampArt) from the player's own game files; nothing at runtime uses these scripts.
They need Node, Python and the web project at $LANDS (~/lands by default), and are kept to compare the C# output against
(`TEX_BASE`/`TEX_OUT` env vars of texture.py). Never ship their output: it is made from the original game art.
