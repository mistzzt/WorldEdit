using System.Collections.Generic;
using OTAPI.Tile;
using TShockAPI;

namespace WorldEdit.Commands
{
	public class Scale : WECommand
	{
		private readonly int _scale;

		public Scale(TSPlayer plr, int scale)
			: base(0, 0, 0, 0, plr)
		{
			_scale = scale;
		}

		public override void Execute()
		{
			var clipboardPath = Tools.GetClipboardPath(plr.User.ID);

			var data = Tools.LoadWorldData(clipboardPath);

			using (var writer = WorldSectionData.WriteHeader(clipboardPath, 0, 0, data.Width * _scale, data.Height * _scale))
			{
				var r = new List<ITile>();
				// TODO: Decreased scaling
				for (var i = 0; i < data.Width; i++)
				{
					for (var j = 0; j < data.Height; j++)
					{
						for (var a = 0; a < _scale; a++)
						{
							writer.Write(data.Tiles[i, j]);
						}
						r.Add(data.Tiles[i, j]);

						if (j != data.Height - 1)
						{
							continue;
						}

						for (var a = 0; a < _scale - 1; a++)
						{
							foreach (var t in r)
							{
								for (var b = 0; b < _scale; b++)
								{
									writer.Write(t);
								}
							}
						}
						r.Clear();
					}
				}
			}

			plr.SendSuccessMessage("Scaled clipboard to {0}x.", _scale);
		}
	}
}
