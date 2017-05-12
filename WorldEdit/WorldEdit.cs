using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using WorldEdit.Commands;
using WorldEdit.Expressions;
using WorldEdit.Extensions;

namespace WorldEdit
{
	public delegate bool Selection(int i, int j, TSPlayer player);

	[ApiVersion(2, 1)]
	public class WorldEdit : TerrariaPlugin
	{
		public static Dictionary<string, int[]> Biomes = new Dictionary<string, int[]>();
		public static Dictionary<string, int> Colors = new Dictionary<string, int>();
		public static IDbConnection Database;
		public static Dictionary<string, Selection> Selections = new Dictionary<string, Selection>();
		public static Dictionary<string, int> Tiles = new Dictionary<string, int>();
		public static Dictionary<string, int> Walls = new Dictionary<string, int>();
		public static Dictionary<string, int> Slopes = new Dictionary<string, int>();

		public override string Author => "Nyx Studios";
		private CancellationTokenSource Cancel = new CancellationTokenSource();
		private BlockingCollection<WECommand> CommandQueue = new BlockingCollection<WECommand>();
		public override string Description => "Adds commands for mass editing of blocks.";
		public override string Name => "WorldEdit";
		public override Version Version => Assembly.GetExecutingAssembly().GetName().Version;

		public WorldEdit(Main game) : base(game)
		{
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
				ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);

				Cancel.Cancel();
			}
		}
		public override void Initialize()
		{
			ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);
			ServerApi.Hooks.NetGetData.Register(this, OnGetData);
		}

		private static void OnGetData(GetDataEventArgs e)
		{
			if (e.Handled)
				return;

			switch (e.MsgID)
			{
				#region Packet 17 - Tile

				case PacketTypes.Tile:
					PlayerInfo info = TShock.Players[e.Msg.whoAmI].GetPlayerInfo();
					if (info.Point != 0)
					{
						using (var reader = new BinaryReader(new MemoryStream(e.Msg.readBuffer, e.Index, e.Length)))
						{
							reader.ReadByte();
							int x = reader.ReadInt16();
							int y = reader.ReadInt16();
							if (x >= 0 && y >= 0 && x < Main.maxTilesX && y < Main.maxTilesY)
							{
								if (info.Point == 1)
								{
									info.X = x;
									info.Y = y;
									TShock.Players[e.Msg.whoAmI].SendInfoMessage("Set point 1.");
								}
								else if (info.Point == 2)
								{
									info.X2 = x;
									info.Y2 = y;
									TShock.Players[e.Msg.whoAmI].SendInfoMessage("Set point 2.");
								}
								else if (info.Point == 3)
								{
									List<string> regions = TShock.Regions.InAreaRegionName(x, y).ToList();
									if (regions.Count == 0)
									{
										TShock.Players[e.Msg.whoAmI].SendErrorMessage("No region exists there.");
										return;
									}
									Region curReg = TShock.Regions.GetRegionByName(regions[0]);
									info.X = curReg.Area.Left;
									info.Y = curReg.Area.Top;
									info.X2 = curReg.Area.Right;
									info.Y2 = curReg.Area.Bottom;
									TShock.Players[e.Msg.whoAmI].SendInfoMessage("Set region.");
								}
								info.Point = 0;
								e.Handled = true;
								TShock.Players[e.Msg.whoAmI].SendTileSquare(x, y, 3);
							}
						}
					}
					return;

				#endregion
				#region Packet 109 - MassWireOperation

				case PacketTypes.MassWireOperation:
					PlayerInfo data = TShock.Players[e.Msg.whoAmI].GetPlayerInfo();
					if (data.Point != 0)
					{
						using (var reader = new BinaryReader(new MemoryStream(e.Msg.readBuffer, e.Index, e.Length)))
						{
							int startX = reader.ReadInt16();
							int startY = reader.ReadInt16();
							int endX = reader.ReadInt16();
							int endY = reader.ReadInt16();

							if (startX >= 0 && startY >= 0 && endX >= 0 && endY >= 0 && startX < Main.maxTilesX && startY < Main.maxTilesY && endX < Main.maxTilesX && endY < Main.maxTilesY)
							{
								if (startX == endX && startY == endY)
								{
									// Set a single point
									if (data.Point == 1)
									{
										data.X = startX;
										data.Y = startY;
										TShock.Players[e.Msg.whoAmI].SendInfoMessage("Set point 1.");
									}
									else if (data.Point == 2)
									{
										data.X2 = startX;
										data.Y2 = startY;
										TShock.Players[e.Msg.whoAmI].SendInfoMessage("Set point 2.");
									}
									else if (data.Point == 3)
									{
										List<string> regions = TShock.Regions.InAreaRegionName(startX, startY).ToList();
										if (regions.Count == 0)
										{
											TShock.Players[e.Msg.whoAmI].SendErrorMessage("No region exists there.");
										}
										else
										{
											Region curReg = TShock.Regions.GetRegionByName(regions[0]);
											data.X = curReg.Area.Left;
											data.Y = curReg.Area.Top;
											data.X2 = curReg.Area.Right;
											data.Y2 = curReg.Area.Bottom;
											TShock.Players[e.Msg.whoAmI].SendInfoMessage("Set region.");
										}
									}
								}
								else
								{
									// Set both points at the same time
									if (data.Point == 1 || data.Point == 2)
									{
										data.X = startX;
										data.Y = startY;
										data.X2 = endX;
										data.Y2 = endY;
										TShock.Players[e.Msg.whoAmI].SendInfoMessage("Set area.");
									}
									else if (data.Point == 3)
									{
										// Set topmost region inside the selection
										int x = Math.Min(startX, endX);
										int y = Math.Min(startY, endY);
										int width = Math.Max(startX, endX) - x;
										int height = Math.Max(startY, endY) - y;
										Rectangle rect = new Rectangle(x, y, width, height);
										List<Region> regions = TShock.Regions.Regions.FindAll(r => rect.Intersects(r.Area));
										if (regions.Count == 0)
										{
											TShock.Players[e.Msg.whoAmI].SendErrorMessage("No region exists there.");
										}
										else
										{
											Region curReg = TShock.Regions.GetTopRegion(regions);
											data.X = curReg.Area.Left;
											data.Y = curReg.Area.Top;
											data.X2 = curReg.Area.Right;
											data.Y2 = curReg.Area.Bottom;
											TShock.Players[e.Msg.whoAmI].SendInfoMessage("Set region.");
										}
									}
								}
								data.Point = 0;
								e.Handled = true;
							}
						}
					}
					return;

					#endregion
			}
		}

		private void OnInitialize(EventArgs e)
		{
			Directory.CreateDirectory("worldedit");

			#region Commands
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.utils.activate", Activate, "/activate")
			{
				HelpText = "Activates non-working signs, chests or item frames."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.selection.all", All, "/all")
			{
				HelpText = "Sets the worldedit selection to the entire world."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.region.biome", Biome, "/biome")
			{
				HelpText = "Converts biomes in the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.clipboard.copy", Copy, "/copy")
			{
				HelpText = "Copies the worldedit selection to the clipboard."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.clipboard.cut", Cut, "/cut")
			{
				HelpText = "Copies the worldedit selection to the clipboard, then deletes it."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.utils.drain", Drain, "/drain")
			{
				HelpText = "Drains liquids in the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.utils.fixghosts", FixGhosts, "/fixghosts")
			{
				HelpText = "Fixes invisible signs, chests and item frames."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.utils.fixgrass", FixGrass, "/fixgrass")
			{
				HelpText = "Fixes suffocated grass in the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.utils.fixhalves", FixHalves, "/fixhalves")
			{
				HelpText = "Fixes half blocks in the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.utils.fixslopes", FixSlopes, "/fixslopes")
			{
				HelpText = "Fixes covered slopes in the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.clipboard.flip", Flip, "/flip")
			{
				HelpText = "Flips the worldedit clipboard."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.utils.flood", Flood, "/flood")
			{
				HelpText = "Floods liquids in the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.utils.mow", Mow, "/mow")
			{
				HelpText = "Mows grass, thorns, and vines in the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.selection.near", Near, "/near")
			{
				AllowServer = false,
				HelpText = "Sets the worldedit selection to a radius around you."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.selection.outline", Outline, "/outline")
			{
				HelpText = "Sets block outline around blocks in area."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.selection.outlinewall", OutlineWall, "/outlinewall")
			{
				HelpText = "Sets wall outline around walls in area."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.region.paint", Paint, "/paint")
			{
				HelpText = "Paints tiles in the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.region.paintwall", PaintWall, "/paintwall")
			{
				HelpText = "Paints walls in the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.clipboard.paste", Paste, "/paste")
			{
				HelpText = "Pastes the clipboard to the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.selection.point", Point1, "/point1")
			{
				HelpText = "Sets the positions of the worldedit selection's first point."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.selection.point", Point2, "/point2")
			{
				HelpText = "Sets the positions of the worldedit selection's second point."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.history.redo", Redo, "/redo")
			{
				HelpText = "Redoes a number of worldedit actions."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.selection.region", RegionCmd, "/region")
			{
				HelpText = "Selects a region as a worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.selection.resize", Resize, "/resize")
			{
				HelpText = "Resizes the worldedit selection in a direction."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.clipboard.rotate", Rotate, "/rotate")
			{
				HelpText = "Rotates the worldedit clipboard."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.schematic", Schematic, "/schematic", "/schem")
			{
				HelpText = "Manages worldedit schematics."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.selection.selecttype", Select, "/select")
			{
				HelpText = "Sets the worldedit selection function."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.region.set", Set, "/set")
			{
				HelpText = "Sets tiles in the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.region.setgrass", SetGrass, "/setgrass")
			{
				HelpText = "Sets certain grass in the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.region.setwall", SetWall, "/setwall")
			{
				HelpText = "Sets walls in the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.region.setwire", SetWire, "/setwire")
			{
				HelpText = "Sets wires in the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.region.slope", Slope, "/slope")
			{
				HelpText = "Slopes tiles in the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.region.delslope", SlopeDelete, "/delslope")
			{
				HelpText = "Removes slopes in the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.region.smooth", Smooth, "/smooth")
			{
				HelpText = "Smooths blocks in the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.selection.inactive", Inactive, "/inactive")
			{
				HelpText = "Sets the inactive status in the worldedit selection."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.selection.shift", Shift, "/shift")
			{
				HelpText = "Shifts the worldedit selection in a direction."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.history.undo", Undo, "/undo")
			{
				HelpText = "Undoes a number of worldedit actions."
			});
			TShockAPI.Commands.ChatCommands.Add(new Command("worldedit.clipboard.scale", Scale, "/scale")
			{
				HelpText = "Scale the clipboard"
			});
			#endregion
			#region Database
			switch (TShock.Config.StorageType.ToLowerInvariant())
			{
				case "mysql":
					string[] host = TShock.Config.MySqlHost.Split(':');
					Database = new MySqlConnection
					{
						ConnectionString = string.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
							host[0],
							host.Length == 1 ? "3306" : host[1],
							TShock.Config.MySqlDbName,
							TShock.Config.MySqlUsername,
							TShock.Config.MySqlPassword)
					};
					break;
				case "sqlite":
					string sql = Path.Combine(TShock.SavePath, "worldedit.sqlite");
					Database = new SqliteConnection(string.Format("uri=file://{0},Version=3", sql));
					break;
			}

			#region Old Version Support
			var lockFilePath = Path.Combine("worldedit", "deleted.lock");
			if (!File.Exists(lockFilePath))
			{
				Database.Query("DROP TABLE WorldEdit");
				foreach (var file in Directory.EnumerateFiles("worldedit", "undo-*.dat"))
				{
					File.Delete(file);
				}
				foreach (var file in Directory.EnumerateFiles("worldedit", "redo-*.dat"))
				{
					File.Delete(file);
				}
				foreach (var file in Directory.EnumerateFiles("worldedit", "clipboard-*.dat"))
				{
					File.Delete(file);
				}
				File.Create(lockFilePath).Close();
				TShock.Log.ConsoleInfo("WorldEdit doesn't support undo/redo/clipboard files that were saved by plugin below version 1.7.");
				TShock.Log.ConsoleInfo("These files had been deleted. However, we still support old schematic files (*.dat) and convert them to new format.");
				TShock.Log.ConsoleInfo("Do not delete deteted.lock inside worldedit folder; this message will only show once.");
			}
			#endregion

			var sqlcreator = new SqlTableCreator(Database,
				Database.GetSqlType() == SqlType.Sqlite ? (IQueryBuilder)new SqliteQueryCreator() : new MysqlQueryCreator());
			sqlcreator.EnsureTableStructure(new SqlTable("WorldEdit",
				new SqlColumn("Account", MySqlDbType.Int32) { Primary = true },
				new SqlColumn("RedoLevel", MySqlDbType.Int32),
				new SqlColumn("UndoLevel", MySqlDbType.Int32)));
			#endregion

			#region Biomes
			// Format: dirt, stone, ice, sand, grass, plants, tall plants, vines, thorn

			Biomes.Add("crimson", new[] { 0, 203, 200, 234, 199, -1, -1, 205, 32 });
			Biomes.Add("flesh", new[] { 0, 203, 200, 234, 199, -1, -1, 205, 32 });
			Biomes.Add("corruption", new[] { 0, 25, 163, 112, 23, 24, -1, -1, 32 });
			Biomes.Add("hallow", new[] { 0, 117, 164, 116, 109, 110, 113, 52, -1 });
			Biomes.Add("jungle", new[] { 59, 1, 161, 53, 60, 61, 74, 62, 69 });
			Biomes.Add("mushroom", new[] { 59, 1, 161, 53, 70, 71, -1, -1, -1 });
			Biomes.Add("normal", new[] { 0, 1, 161, 53, 2, 3, 73, 52, -1 });
			Biomes.Add("forest", new[] { 0, 1, 161, 53, 2, 3, 73, 52, -1 });
			Biomes.Add("snow", new[] { 147, 161, 161, 53, 147, -1, -1, -1, -1 });
			#endregion
			#region Colors
			Colors.Add("blank", 0);

			Main.player[Main.myPlayer] = new Player();
			var item = new Item();
			for (var i = 1; i < Main.maxItemTypes; i++)
			{
				item.netDefaults(i);

				if (item.paint <= 0)
				{
					continue;
				}

				var name = TShockAPI.Localization.EnglishLanguage.GetItemNameById(i);
				Colors.Add(name.Substring(0, name.Length - 6).ToLowerInvariant(), item.paint);
			}
			#endregion
			#region Selections
			Selections.Add("altcheckers", (i, j, plr) => ((i + j) & 1) == 0);
			Selections.Add("checkers", (i, j, plr) => ((i + j) & 1) == 1);
			Selections.Add("ellipse", (i, j, plr) =>
			{
				PlayerInfo info = plr.GetPlayerInfo();

				int X = Math.Min(info.X, info.X2);
				int Y = Math.Min(info.Y, info.Y2);
				int X2 = Math.Max(info.X, info.X2);
				int Y2 = Math.Max(info.Y, info.Y2);

				Vector2 center = new Vector2((float)(X2 - X) / 2, (float)(Y2 - Y) / 2);
				float major = Math.Max(center.X, center.Y);
				float minor = Math.Min(center.X, center.Y);
				if (center.Y > center.X)
				{
					float temp = major;
					major = minor;
					minor = temp;
				}
				return (i - center.X - X) * (i - center.X - X) / (major * major) + (j - center.Y - Y) * (j - center.Y - Y) / (minor * minor) <= 1;
			});
			Selections.Add("normal", (i, j, plr) => true);
			Selections.Add("border", (i, j, plr) =>
			{
				PlayerInfo info = plr.GetPlayerInfo();
				return i == info.X || i == info.X2 || j == info.Y || j == info.Y2;
			});
			Selections.Add("outline", (i, j, plr) =>
			{
				return ((i > 0) && (j > 0) && (i < Main.maxTilesX - 1) && (j < Main.maxTilesY - 1)
					&& (Main.tile[i, j].active())
					&& ((!Main.tile[i - 1, j].active()) || (!Main.tile[i, j - 1].active())
					|| (!Main.tile[i + 1, j].active()) || (!Main.tile[i, j + 1].active())
					|| (!Main.tile[i + 1, j + 1].active()) || (!Main.tile[i - 1, j - 1].active())
					|| (!Main.tile[i - 1, j + 1].active()) || (!Main.tile[i + 1, j - 1].active())));
			});
			Selections.Add("45", (i, j, plr) =>
			{
				PlayerInfo info = plr.GetPlayerInfo();

				int X = Math.Min(info.X, info.X2);
				int Y = Math.Min(info.Y, info.Y2);

				return (i - X) == (j - Y);
			});
			Selections.Add("225", (i, j, plr) =>
			{
				PlayerInfo info = plr.GetPlayerInfo();

				int Y = Math.Min(info.Y, info.Y2);
				int X2 = Math.Max(info.X, info.X2);

				return (X2 - i) == (j - Y);
			});
			#endregion
			#region Tiles
			Tiles.Add("air", -1);
			Tiles.Add("lava", -2);
			Tiles.Add("honey", -3);
			Tiles.Add("water", -4);

			foreach (var fi in typeof(TileID).GetFields())
			{
				string name = fi.Name;
				var sb = new StringBuilder();
				for (int i = 0; i < name.Length; i++)
				{
					if (char.IsUpper(name[i]))
						sb.Append(" ").Append(char.ToLower(name[i]));
					else
						sb.Append(name[i]);
				}
				Tiles.Add(sb.ToString(1, sb.Length - 1), (ushort)fi.GetValue(null));
			}
			#endregion
			#region Walls
			Walls.Add("air", 0);

			foreach (var fi in typeof(WallID).GetFields())
			{
				string name = fi.Name;
				var sb = new StringBuilder();
				for (int i = 0; i < name.Length; i++)
				{
					if (char.IsUpper(name[i]))
						sb.Append(" ").Append(char.ToLower(name[i]));
					else
						sb.Append(name[i]);
				}
				Walls.Add(sb.ToString(1, sb.Length - 1), (byte)fi.GetValue(null));
			}
			#endregion
			#region Slopes
			Slopes.Add("none", 0);
			Slopes.Add("t", 1);
			Slopes.Add("tr", 2);
			Slopes.Add("ur", 2);
			Slopes.Add("tl", 3);
			Slopes.Add("ul", 3);
			Slopes.Add("br", 4);
			Slopes.Add("dr", 4);
			Slopes.Add("bl", 5);
			Slopes.Add("dl", 5);
			#endregion
			ThreadPool.QueueUserWorkItem(QueueCallback);
		}

		private void QueueCallback(object context)
		{
			while (!Netplay.disconnect)
			{
				try
				{
					if (!CommandQueue.TryTake(out var command, -1, Cancel.Token))
						return;
					if (Main.rand == null)
						Main.rand = new UnifiedRandom();
					command.Position();
					command.Execute();
				}
				catch (OperationCanceledException)
				{
					return;
				}
			}
		}

		private void Activate(CommandArgs e)
		{
			if (e.Parameters.Count != 1)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //activate <sign / chest / itemframe>");
				return;
			}

			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
			{
				e.Player.SendErrorMessage("Invalid selection.");
				return;
			}

			int Action;
			switch (e.Parameters[0].ToLowerInvariant())
			{
				case "sign":
					{
						Action = 0;
						break;
					}
				case "chest":
					{
						Action = 1;
						break;
					}
				case "item":
				case "frame":
				case "itemframe":
					{
						Action = 2;
						break;
					}
				default:
					{
						e.Player.SendErrorMessage("Invalid activation type '{0}'.", e.Parameters[0]);
						return;
					}
			}

			CommandQueue.Add(new Activate(info.X, info.Y, info.X2, info.Y2, e.Player, Action));
		}

		private void All(CommandArgs e)
		{
			PlayerInfo info = e.Player.GetPlayerInfo();
			info.X = info.Y = 0;
			info.X2 = Main.maxTilesX - 1;
			info.Y2 = Main.maxTilesY - 1;
			e.Player.SendSuccessMessage("Selected all tiles.");
		}

		private void Biome(CommandArgs e)
		{
			if (e.Parameters.Count != 2)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //biome <biome 1> <biome 2>");
				return;
			}
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
			{
				e.Player.SendErrorMessage("Invalid selection.");
				return;
			}

			string biome1 = e.Parameters[0].ToLowerInvariant();
			string biome2 = e.Parameters[1].ToLowerInvariant();
			if (!Biomes.ContainsKey(biome1) || !Biomes.ContainsKey(biome2))
				e.Player.SendErrorMessage("Invalid biome.");
			else
				CommandQueue.Add(new Biome(info.X, info.Y, info.X2, info.Y2, e.Player, biome1, biome2));
		}

		private void Copy(CommandArgs e)
		{
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
				e.Player.SendErrorMessage("Invalid selection!");
			else
				CommandQueue.Add(new Copy(info.X, info.Y, info.X2, info.Y2, e.Player));
		}

		private void Cut(CommandArgs e)
		{
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
				e.Player.SendErrorMessage("Invalid selection.");
			else
				CommandQueue.Add(new Cut(info.X, info.Y, info.X2, info.Y2, e.Player));
		}

		private void Drain(CommandArgs e)
		{
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
				e.Player.SendErrorMessage("Invalid selection.");
			else
				CommandQueue.Add(new Drain(info.X, info.Y, info.X2, info.Y2, e.Player));
		}

		private void FixGhosts(CommandArgs e)
		{
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
				e.Player.SendErrorMessage("Invalid selection!");
			else
				CommandQueue.Add(new FixGhosts(info.X, info.Y, info.X2, info.Y2, e.Player));
		}

		private void FixGrass(CommandArgs e)
		{
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
				e.Player.SendErrorMessage("Invalid selection!");
			else
				CommandQueue.Add(new FixGrass(info.X, info.Y, info.X2, info.Y2, e.Player));
		}

		private void FixHalves(CommandArgs e)
		{
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
				e.Player.SendErrorMessage("Invalid selection!");
			else
				CommandQueue.Add(new FixHalves(info.X, info.Y, info.X2, info.Y2, e.Player));
		}

		private void FixSlopes(CommandArgs e)
		{
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
				e.Player.SendErrorMessage("Invalid selection!");
			else
				CommandQueue.Add(new FixSlopes(info.X, info.Y, info.X2, info.Y2, e.Player));
		}

		private void Flood(CommandArgs e)
		{
			if (e.Parameters.Count != 1)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //flood <liquid>");
				return;
			}

			int liquid = 0;
			if (string.Equals(e.Parameters[0], "lava", StringComparison.CurrentCultureIgnoreCase))
				liquid = 1;
			else if (string.Equals(e.Parameters[0], "honey", StringComparison.CurrentCultureIgnoreCase))
				liquid = 2;
			else if (!string.Equals(e.Parameters[0], "water", StringComparison.CurrentCultureIgnoreCase))
			{
				e.Player.SendErrorMessage("Invalid liquid type '{0}'!", e.Parameters[0]);
				return;
			}

			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
				e.Player.SendErrorMessage("Invalid selection!");
			CommandQueue.Add(new Flood(info.X, info.Y, info.X2, info.Y2, e.Player, liquid));
		}

		private void Flip(CommandArgs e)
		{
			if (e.Parameters.Count != 1)
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //flip <direction>");
			else if (!Tools.HasClipboard(e.Player.User.ID))
				e.Player.SendErrorMessage("Invalid clipboard!");
			else
			{
				bool flipX = false;
				bool flipY = false;
				foreach (char c in e.Parameters[0].ToLowerInvariant())
				{
					if (c == 'x')
						flipX ^= true;
					else if (c == 'y')
						flipY ^= true;
					else
					{
						e.Player.SendErrorMessage("Invalid direction '{0}'!", c);
						return;
					}
				}
				CommandQueue.Add(new Flip(e.Player, flipX, flipY));
			}
		}

		private void Mow(CommandArgs e)
		{
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
				e.Player.SendErrorMessage("Invalid selection!");
			else
				CommandQueue.Add(new Mow(info.X, info.Y, info.X2, info.Y2, e.Player));
		}

		private void Near(CommandArgs e)
		{
			if (e.Parameters.Count != 1)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //near <radius>");
				return;
			}

			int radius;
			if (!int.TryParse(e.Parameters[0], out radius) || radius <= 0)
			{
				e.Player.SendErrorMessage("Invalid radius '{0}'!", e.Parameters[0]);
				return;
			}

			PlayerInfo info = e.Player.GetPlayerInfo();
			info.X = e.Player.TileX - radius;
			info.X2 = e.Player.TileX + radius + 1;
			info.Y = e.Player.TileY - radius;
			info.Y2 = e.Player.TileY + radius + 2;
			e.Player.SendSuccessMessage("Selected tiles around you!");
		}

		private void Outline(CommandArgs e)
		{
			if (e.Parameters.Count < 3)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //outline <tile> <color> <state> [=> boolean expr...]");
				return;
			}
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
			{
				e.Player.SendErrorMessage("Invalid selection!");
				return;
			}

			var colors = Tools.GetColorID(e.Parameters[1].ToLowerInvariant());
			if (colors.Count == 0)
				e.Player.SendErrorMessage("Invalid color '{0}'!", e.Parameters[0]);
			else if (colors.Count > 1)
				e.Player.SendErrorMessage("More than one color matched!");
			else
			{
				bool state = false;
				if (string.Equals(e.Parameters[2], "active", StringComparison.CurrentCultureIgnoreCase))
					state = true;
				else if (string.Equals(e.Parameters[2], "a", StringComparison.CurrentCultureIgnoreCase))
					state = true;
				else if (string.Equals(e.Parameters[2], "na", StringComparison.CurrentCultureIgnoreCase))
					state = false;
				else if (!string.Equals(e.Parameters[2], "nactive", StringComparison.CurrentCultureIgnoreCase))
				{
					e.Player.SendErrorMessage("Invalid active state '{0}'!", e.Parameters[1]);
					return;
				}

				var tiles = Tools.GetTileID(e.Parameters[0].ToLowerInvariant());
				if (tiles.Count == 0)
					e.Player.SendErrorMessage("Invalid tile '{0}'!", e.Parameters[0]);
				else if (tiles.Count > 1)
					e.Player.SendErrorMessage("More than one tile matched!");
				else
				{
					Expression expression = null;
					if (e.Parameters.Count > 3)
					{
						if (!Parser.TryParseTree(e.Parameters.Skip(3), out expression))
						{
							e.Player.SendErrorMessage("Invalid expression!");
							return;
						}
					}
					CommandQueue.Add(new Outline(info.X, info.Y, info.X2, info.Y2, e.Player, tiles[0], colors[0], state, expression));
				}
			}
		}

		private void OutlineWall(CommandArgs e)
		{
			if (e.Parameters.Count < 2)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //outlinewall <wall> [color] [=> boolean expr...]");
				return;
			}
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
			{
				e.Player.SendErrorMessage("Invalid selection!");
				return;
			}

			var colors = Tools.GetColorID(e.Parameters[1].ToLowerInvariant());
			if (colors.Count == 0)
				e.Player.SendErrorMessage("Invalid color '{0}'!", e.Parameters[0]);
			else if (colors.Count > 1)
				e.Player.SendErrorMessage("More than one color matched!");
			else
			{
				var walls = Tools.GetWallID(e.Parameters[0].ToLowerInvariant());
				if (walls.Count == 0)
					e.Player.SendErrorMessage("Invalid wall '{0}'!", e.Parameters[0]);
				else if (walls.Count > 1)
					e.Player.SendErrorMessage("More than one wall matched!");
				else
				{
					Expression expression = null;
					if (e.Parameters.Count > 2)
					{
						if (!Parser.TryParseTree(e.Parameters.Skip(2), out expression))
						{
							e.Player.SendErrorMessage("Invalid expression!");
							return;
						}
					}
					CommandQueue.Add(new OutlineWall(info.X, info.Y, info.X2, info.Y2, e.Player, walls[0], colors[0], expression));
				}
			}
		}

		private void Paint(CommandArgs e)
		{
			if (e.Parameters.Count == 0)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //paint <color> [where] [conditions...]");
				return;
			}
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
			{
				e.Player.SendErrorMessage("Invalid selection!");
				return;
			}

			var colors = Tools.GetColorID(e.Parameters[0].ToLowerInvariant());
			if (colors.Count == 0)
				e.Player.SendErrorMessage("Invalid color '{0}'!", e.Parameters[0]);
			else if (colors.Count > 1)
				e.Player.SendErrorMessage("More than one color matched!");
			else
			{
				Expression expression = null;
				if (e.Parameters.Count > 1)
				{
					if (!Parser.TryParseTree(e.Parameters.Skip(1), out expression))
					{
						e.Player.SendErrorMessage("Invalid expression!");
						return;
					}
				}
				CommandQueue.Add(new Paint(info.X, info.Y, info.X2, info.Y2, e.Player, colors[0], expression));
			}
		}

		private void PaintWall(CommandArgs e)
		{
			if (e.Parameters.Count == 0)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //paintwall <color> [where] [conditions...]");
				return;
			}
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
			{
				e.Player.SendErrorMessage("Invalid selection!");
				return;
			}

			var colors = Tools.GetColorID(e.Parameters[0].ToLowerInvariant());
			if (colors.Count == 0)
				e.Player.SendErrorMessage("Invalid color '{0}'!", e.Parameters[0]);
			else if (colors.Count > 1)
				e.Player.SendErrorMessage("More than one color matched!");
			else
			{
				Expression expression = null;
				if (e.Parameters.Count > 1)
				{
					if (!Parser.TryParseTree(e.Parameters.Skip(1), out expression))
					{
						e.Player.SendErrorMessage("Invalid expression!");
						return;
					}
				}
				CommandQueue.Add(new PaintWall(info.X, info.Y, info.X2, info.Y2, e.Player, colors[0], expression));
			}
		}

		private void Paste(CommandArgs e)
		{
			PlayerInfo info = e.Player.GetPlayerInfo();
			e.Player.SendInfoMessage("X: {0}, Y: {1}", info.X, info.Y);
			if (info.X == -1 || info.Y == -1)
				e.Player.SendErrorMessage("Invalid first point!");
			else if (!Tools.HasClipboard(e.Player.User.ID))
				e.Player.SendErrorMessage("Invalid clipboard!");
			else
			{
				int alignment = 0;
				if (e.Parameters.Count == 1)
				{
					foreach (char c in e.Parameters[0].ToLowerInvariant())
					{
						if (c == 'l')
							alignment &= 2;
						else if (c == 'r')
							alignment |= 1;
						else if (c == 't')
							alignment &= 1;
						else if (c == 'b')
							alignment |= 2;
						else
						{
							e.Player.SendErrorMessage("Invalid paste alignment '{0}'!", c);
							return;
						}
					}
				}

				Expression expression = null;
				if (e.Parameters.Count > 1)
				{
					if (!Parser.TryParseTree(e.Parameters.Skip(1), out expression))
					{
						e.Player.SendErrorMessage("Invalid expression!");
						return;
					}
				}
				CommandQueue.Add(new Paste(info.X, info.Y, e.Player, alignment, expression));
			}
		}

		private void Point1(CommandArgs e)
		{
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (e.Parameters.Count == 0)
			{
				if (!e.Player.RealPlayer)
					e.Player.SendErrorMessage("You must use this command in-game.");
				else
				{
					info.Point = 1;
					e.Player.SendInfoMessage("Modify a block to set point 1.");
				}
				return;
			}
			if (e.Parameters.Count != 2)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //point1 <x> <y>");
				return;
			}

			int x, y;
			if (!int.TryParse(e.Parameters[0], out x) || x < 0 || x >= Main.maxTilesX
				|| !int.TryParse(e.Parameters[1], out y) || y < 0 || y >= Main.maxTilesY)
			{
				e.Player.SendErrorMessage("Invalid coordinates.");
				return;
			}

			info.X = x;
			info.Y = y;
			e.Player.SendInfoMessage("Set point 1.");
		}

		private void Point2(CommandArgs e)
		{
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (e.Parameters.Count == 0)
			{
				if (!e.Player.RealPlayer)
					e.Player.SendErrorMessage("You must use this command in-game.");
				else
				{
					info.Point = 2;
					e.Player.SendInfoMessage("Modify a block to set point 2.");
				}
				return;
			}
			if (e.Parameters.Count != 2)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //point2 [x] [y]");
				return;
			}

			int x, y;
			if (!int.TryParse(e.Parameters[0], out x) || x < 0 || x >= Main.maxTilesX
				|| !int.TryParse(e.Parameters[1], out y) || y < 0 || y >= Main.maxTilesY)
			{
				e.Player.SendErrorMessage("Invalid coordinates '({0}, {1})'!", e.Parameters[0], e.Parameters[1]);
				return;
			}

			info.X2 = x;
			info.Y2 = y;
			e.Player.SendInfoMessage("Set point 2.");
		}

		private void Redo(CommandArgs e)
		{
			if (e.Parameters.Count > 2)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //redo [steps] [account]");
				return;
			}

			int steps = 1;
			int ID = e.Player.User.ID;
			if (e.Parameters.Count > 0 && (!int.TryParse(e.Parameters[0], out steps) || steps <= 0))
				e.Player.SendErrorMessage("Invalid redo steps '{0}'!", e.Parameters[0]);
			else
			{
				if (e.Parameters.Count > 1)
				{
					User User = TShock.Users.GetUserByName(e.Parameters[1]);
					if (User == null)
					{
						e.Player.SendErrorMessage("Invalid account name!");
						return;
					}
					ID = User.ID;
				}
			}
			CommandQueue.Add(new Redo(e.Player, ID, steps));
		}

		private void RegionCmd(CommandArgs e)
		{
			if (e.Parameters.Count > 1)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //region [region name]");
				return;
			}

			PlayerInfo info = e.Player.GetPlayerInfo();
			if (e.Parameters.Count == 0)
			{
				info.Point = 3;
				e.Player.SendInfoMessage("Hit a block to select that region.");
			}
			else
			{
				Region region = TShock.Regions.GetRegionByName(e.Parameters[0]);
				if (region == null)
					e.Player.SendErrorMessage("Invalid region '{0}'!", e.Parameters[0]);
				else
				{
					info.X = region.Area.Left;
					info.Y = region.Area.Top;
					info.X2 = region.Area.Right;
					info.Y2 = region.Area.Bottom;
					e.Player.SendSuccessMessage("Set selection to region '{0}'.", region.Name);
				}
			}
		}

		private void Resize(CommandArgs e)
		{
			if (e.Parameters.Count != 2)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //resize <direction(s)> <amount>");
				return;
			}
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
			{
				e.Player.SendErrorMessage("Invalid selection!");
				return;
			}

			int amount;
			if (!int.TryParse(e.Parameters[1], out amount))
			{
				e.Player.SendErrorMessage("Invalid resize amount '{0}'!", e.Parameters[0]);
				return;
			}

			foreach (char c in e.Parameters[0].ToLowerInvariant())
			{
				if (c == 'd')
				{
					if (info.Y < info.Y2)
						info.Y2 += amount;
					else
						info.Y += amount;
				}
				else if (c == 'l')
				{
					if (info.X < info.X2)
						info.X -= amount;
					else
						info.X2 -= amount;
				}
				else if (c == 'r')
				{
					if (info.X < info.X2)
						info.X2 += amount;
					else
						info.X += amount;
				}
				else if (c == 'u')
				{
					if (info.Y < info.Y2)
						info.Y -= amount;
					else
						info.Y2 -= amount;
				}
				else
				{
					e.Player.SendErrorMessage("Invalid direction '{0}'!", c);
					return;
				}
			}
			e.Player.SendSuccessMessage("Resized selection.");
		}

		private void Rotate(CommandArgs e)
		{
			if (e.Parameters.Count != 1)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //rotate <angle>");
				return;
			}
			if (!Tools.HasClipboard(e.Player.User.ID))
			{
				e.Player.SendErrorMessage("Invalid clipboard!");
				return;
			}

			int degrees;
			if (!int.TryParse(e.Parameters[0], out degrees) || degrees % 90 != 0)
				e.Player.SendErrorMessage("Invalid angle '{0}'!", e.Parameters[0]);
			else
				CommandQueue.Add(new Rotate(e.Player, degrees));
		}

		private void Scale(CommandArgs e)
		{
			if (e.Parameters.Count != 1)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //scale <amount>");
				return;
			}
			if (!Tools.HasClipboard(e.Player.User.ID))
			{
				e.Player.SendErrorMessage("Invalid clipboard!");
				return;
			}
			if (!int.TryParse(e.Parameters[0], out int scale))
			{
				e.Player.SendErrorMessage("Invalid amount!");
				return;
			}
			CommandQueue.Add(new Scale(e.Player, scale));
		}

		private void Schematic(CommandArgs e)
		{
			const string schemFileNameFormat = "schematic-{0}.schem";
			const string schemFileNameFormatOld = "schematic-{0}.dat";

			string subCmd = e.Parameters.Count == 0 ? "help" : e.Parameters[0].ToLowerInvariant();
			switch (subCmd)
			{
				case "del":
				case "delete":
					{
						if (e.Parameters.Count != 2)
						{
							e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //schematic delete <name>");
							return;
						}

						string old = Path.Combine("worldedit", string.Format(schemFileNameFormatOld, e.Parameters[1]));
						string path = Path.Combine("worldedit", string.Format(schemFileNameFormat, e.Parameters[1]));

						if (!File.Exists(old) && !File.Exists(path))
						{
							e.Player.SendErrorMessage("Invalid schematic '{0}'!");
							return;
						}

						File.Delete(File.Exists(path) ? path : old);
						e.Player.SendErrorMessage("Deleted schematic '{0}'.", e.Parameters[1]);
					}
					return;
				case "help":
					e.Player.SendSuccessMessage("Schematics Subcommands:");
					e.Player.SendInfoMessage("//schem delete/del <name>\r\n"
										   + "//schem list [page]\r\n"
										   + "//schem load/l <name>\r\n"
										   + "//schem save/s <name>\r\n");
					return;
				case "list":
					{
						if (e.Parameters.Count > 2)
						{
							e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //schematic list [page]");
							return;
						}

						int pageNumber;
						if (!PaginationTools.TryParsePageNumber(e.Parameters, 1, e.Player, out pageNumber))
							return;

						var oldschematics = from s in Directory.EnumerateFiles("worldedit", schemFileNameFormatOld)
											select s.Substring(20, s.Length - 24);
						var newschematics = from s in Directory.EnumerateFiles("worldedit", schemFileNameFormat)
											select s.Substring(24, s.Length - 28);
						var schematics = newschematics.Concat(oldschematics).Distinct();

						PaginationTools.SendPage(e.Player, pageNumber, PaginationTools.BuildLinesFromTerms(schematics),
							new PaginationTools.Settings
							{
								HeaderFormat = "Schematics ({0}/{1}):",
								FooterFormat = "Type //schematic list {0} for more."
							});
					}
					return;
				case "l":
				case "load":
					{
						if (e.Parameters.Count != 2)
						{
							e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //schematic load <name>");
							return;
						}

						string old = Path.Combine("worldedit", string.Format(schemFileNameFormatOld, e.Parameters[1]));
						string path = Path.Combine("worldedit", string.Format(schemFileNameFormat, e.Parameters[1]));

						var clipboard = Tools.GetClipboardPath(e.Player.User.ID);

						if (File.Exists(path))
						{
							File.Copy(path, clipboard, true);
						}
						else if (File.Exists(old))
						{
							var tiles = Tools.LoadWorldDataOld(old);
							var data = new WorldSectionData(tiles.GetLength(0), tiles.GetLength(1)) { Tiles = tiles };
							data.Write(clipboard);
						}
						else
						{
							e.Player.SendErrorMessage("Invalid schematic '{0}'!");
							return;
						}

						e.Player.SendSuccessMessage("Loaded schematic '{0}' to clipboard.", e.Parameters[1]);
					}
					return;
				case "s":
				case "save":
					{
						if (e.Parameters.Count != 2)
						{
							e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //schematic save <name>");
							return;
						}

						string clipboard = Path.Combine("worldedit", string.Format("clipboard-{0}.dat", e.Player.User.ID));

						if (!File.Exists(clipboard))
						{
							e.Player.SendErrorMessage("Invalid clipboard!");
							return;
						}

						if (!Tools.IsCorrectName(e.Parameters[1]))
						{
							e.Player.SendErrorMessage("Name should not contain these symbols: \"{0}\".",
								string.Join("\", \"", Path.GetInvalidFileNameChars()));
							return;
						}

						string Old = Path.Combine("worldedit", string.Format("schematic-{0}.dat", e.Parameters[1]));
						string New = Path.Combine("worldedit", string.Format("schematic-new-{0}.dat", e.Parameters[1]));

						File.Copy(clipboard, New, true);

						if (File.Exists(Old))
						{ File.Delete(Old); }

						e.Player.SendSuccessMessage("Saved clipboard to schematic '{0}'.", e.Parameters[1]);
					}
					return;
				default:
					e.Player.SendErrorMessage("Invalid subcommand.");
					return;
			}
		}

		private void Select(CommandArgs e)
		{
			if (e.Parameters.Count != 1)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //select <selection type>");
				e.Player.SendInfoMessage("Available selections: " + string.Join(", ", Selections.Keys) + ".");
				return;
			}

			if (e.Parameters[0].ToLowerInvariant() == "help")
			{
				e.Player.SendInfoMessage("Proper syntax: //select <selection type>");
				e.Player.SendInfoMessage("Available selections: " + string.Join(", ", Selections.Keys) + ".");
				return;
			}

			string selection = e.Parameters[0].ToLowerInvariant();
			if (!Selections.ContainsKey(selection))
			{
				string available = "Available selections: " + string.Join(", ", Selections.Keys) + ".";
				e.Player.SendErrorMessage("Invalid selection type '{0}'!\r\n{1}", selection, available);
				return;
			}
			e.Player.GetPlayerInfo().Select = Selections[selection];
			e.Player.SendSuccessMessage("Set selection type to '{0}'.", selection);
		}

		private void Set(CommandArgs e)
		{
			if (e.Parameters.Count == 0)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //set <tile> [=> boolean expr...]");
				return;
			}
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
			{
				e.Player.SendErrorMessage("Invalid selection!");
				return;
			}

			var tiles = Tools.GetTileID(e.Parameters[0].ToLowerInvariant());
			if (tiles.Count == 0)
				e.Player.SendErrorMessage("Invalid tile '{0}'!", e.Parameters[0]);
			else if (tiles.Count > 1)
				e.Player.SendErrorMessage("More than one tile matched!");
			else
			{
				Expression expression = null;
				if (e.Parameters.Count > 1)
				{
					if (!Parser.TryParseTree(e.Parameters.Skip(1), out expression))
					{
						e.Player.SendErrorMessage("Invalid expression!");
						return;
					}
				}
				CommandQueue.Add(new Set(info.X, info.Y, info.X2, info.Y2, e.Player, tiles[0], expression));
			}
		}

		private void SetWall(CommandArgs e)
		{
			if (e.Parameters.Count == 0)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //setwall <wall> [=> boolean expr...]");
				return;
			}
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
			{
				e.Player.SendErrorMessage("Invalid selection!");
				return;
			}

			var walls = Tools.GetWallID(e.Parameters[0].ToLowerInvariant());
			if (walls.Count == 0)
				e.Player.SendErrorMessage("Invalid wall '{0}'!", e.Parameters[0]);
			else if (walls.Count > 1)
				e.Player.SendErrorMessage("More than one wall matched!");
			else
			{
				Expression expression = null;
				if (e.Parameters.Count > 1)
				{
					if (!Parser.TryParseTree(e.Parameters.Skip(1), out expression))
					{
						e.Player.SendErrorMessage("Invalid expression!");
						return;
					}
				}
				CommandQueue.Add(new SetWall(info.X, info.Y, info.X2, info.Y2, e.Player, walls[0], expression));
			}
		}

		private void SetGrass(CommandArgs e)
		{
			if (e.Parameters.Count == 0)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //setgrass <grass> [=> boolean expr...]");
				return;
			}
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
			{
				e.Player.SendErrorMessage("Invalid selection!");
				return;
			}

			if (!Biomes.Keys.Contains(e.Parameters[0].ToLowerInvariant()) || (e.Parameters[0].ToLowerInvariant() == "snow"))
			{
				e.Player.SendErrorMessage("Invalid grass '{0}'!", e.Parameters[0]);
				return;
			}

			Expression expression = null;
			if (e.Parameters.Count > 1)
			{
				if (!Parser.TryParseTree(e.Parameters.Skip(1), out expression))
				{
					e.Player.SendErrorMessage("Invalid expression!");
					return;
				}
			}

			CommandQueue.Add(new SetGrass(info.X, info.Y, info.X2, info.Y2, e.Player, e.Parameters[0].ToLowerInvariant(), expression));
		}

		private void SetWire(CommandArgs e)
		{
			if (e.Parameters.Count < 2)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //setwire <wire> <wire state> [=> boolean expr...]");
				return;
			}
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
			{
				e.Player.SendErrorMessage("Invalid selection!");
				return;
			}

			int wire;
			if (!int.TryParse(e.Parameters[0], out wire) || wire < 1 || wire > 4)
			{
				e.Player.SendErrorMessage("Invalid wire '{0}'!", e.Parameters[0]);
				return;
			}

			bool state = false;
			if (string.Equals(e.Parameters[1], "on", StringComparison.CurrentCultureIgnoreCase))
				state = true;
			else if (!string.Equals(e.Parameters[1], "off", StringComparison.CurrentCultureIgnoreCase))
			{
				e.Player.SendErrorMessage("Invalid wire state '{0}'!", e.Parameters[1]);
				return;
			}

			Expression expression = null;
			if (e.Parameters.Count > 2)
			{
				if (!Parser.TryParseTree(e.Parameters.Skip(2), out expression))
				{
					e.Player.SendErrorMessage("Invalid expression!");
					return;
				}
			}
			CommandQueue.Add(new SetWire(info.X, info.Y, info.X2, info.Y2, e.Player, wire, state, expression));
		}

		private void Slope(CommandArgs e)
		{
			if (e.Parameters.Count == 0)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //slope <type> [=> boolean expr...]");
				return;
			}
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
			{
				e.Player.SendErrorMessage("Invalid selection!");
				return;
			}

			int slope = Tools.GetSlopeID(e.Parameters[0].ToLowerInvariant());
			if (slope == -1)
				e.Player.SendErrorMessage("Invalid type '{0}'! Available slopes: " +
					"none (0), t (1), tr (2), tl (3), br (4), bl (5)", e.Parameters[0]);
			else
			{
				Expression expression = null;
				if (e.Parameters.Count > 1)
				{
					if (!Parser.TryParseTree(e.Parameters.Skip(1), out expression))
					{
						e.Player.SendErrorMessage("Invalid expression!");
						return;
					}
				}
				CommandQueue.Add(new Slope(info.X, info.Y, info.X2, info.Y2, e.Player, slope, expression));
			}
		}

		private void SlopeDelete(CommandArgs e)
		{
			int slope = 255;
			Expression expression = null;
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
			{
				e.Player.SendErrorMessage("Invalid selection!");
				return;
			}
			if (e.Parameters.Count >= 1)
			{
				slope = Tools.GetSlopeID(e.Parameters[0].ToLowerInvariant());
				if (slope == -1)
				{
					e.Player.SendErrorMessage("Invalid type '{0}'! Available slopes: " +
						"none (0), t (1), tr (2), tl (3), br (4), bl (5)", e.Parameters[0]);
					return;
				}
				if (e.Parameters.Count > 1)
				{
					if (!Parser.TryParseTree(e.Parameters.Skip(1), out expression))
					{
						e.Player.SendErrorMessage("Invalid expression!");
						return;
					}
				}
			}

			CommandQueue.Add(new SlopeDelete(info.X, info.Y, info.X2, info.Y2, e.Player, slope, expression));
		}

		private void Smooth(CommandArgs e)
		{
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
			{
				e.Player.SendErrorMessage("Invalid selection!");
				return;
			}

			bool Plus = false;
			int Expr = 0;
			Expression expression = null;
			if (e.Parameters.Count > 0)
			{
				if (e.Parameters[0] == "+")
				{
					Plus = true;
					Expr = 1;
				}
				if (e.Parameters.Count > Expr)
				{
					if (!Parser.TryParseTree(e.Parameters.Skip(Expr), out expression))
					{
						e.Player.SendErrorMessage("Invalid expression!");
						return;
					}
				}
			}

			CommandQueue.Add(new Smooth(info.X, info.Y, info.X2, info.Y2, e.Player, expression, Plus));
		}

		private void Inactive(CommandArgs e)
		{
			if (e.Parameters.Count == 0)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //inactive <status(on/off/reverse)> [=> boolean expr...]");
				return;
			}
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
			{
				e.Player.SendErrorMessage("Invalid selection!");
				return;
			}

			int mode = 2;
			var modeName = e.Parameters[0].ToLower();
			if (modeName == "on")
				mode = 0;
			else if (modeName == "off")
				mode = 1;
			else if (modeName != "reverse")
			{
				e.Player.SendErrorMessage("Invalid status! Proper: on, off, reverse");
				return;
			}
			Expression expression = null;
			if (e.Parameters.Count > 1)
			{
				if (!Parser.TryParseTree(e.Parameters.Skip(1), out expression))
				{
					e.Player.SendErrorMessage("Invalid expression!");
					return;
				}
			}
			CommandQueue.Add(new Inactive(info.X, info.Y, info.X2, info.Y2, e.Player, mode, expression));
		}

		private void Shift(CommandArgs e)
		{
			if (e.Parameters.Count != 2)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //shift <direction> <amount>");
				return;
			}
			PlayerInfo info = e.Player.GetPlayerInfo();
			if (info.X == -1 || info.Y == -1 || info.X2 == -1 || info.Y2 == -1)
			{
				e.Player.SendErrorMessage("Invalid selection!");
				return;
			}

			int amount;
			if (!int.TryParse(e.Parameters[1], out amount) || amount < 0)
			{
				e.Player.SendErrorMessage("Invalid shift amount '{0}'!", e.Parameters[0]);
				return;
			}

			foreach (char c in e.Parameters[0].ToLowerInvariant())
			{
				if (c == 'd')
				{
					info.Y += amount;
					info.Y2 += amount;
				}
				else if (c == 'l')
				{
					info.X -= amount;
					info.X2 -= amount;
				}
				else if (c == 'r')
				{
					info.X += amount;
					info.X2 += amount;
				}
				else if (c == 'u')
				{
					info.Y -= amount;
					info.Y2 -= amount;
				}
				else
				{
					e.Player.SendErrorMessage("Invalid direction '{0}'!", c);
					return;
				}
			}
			e.Player.SendSuccessMessage("Shifted selection.");
		}

		private void Undo(CommandArgs e)
		{
			if (e.Parameters.Count > 2)
			{
				e.Player.SendErrorMessage("Invalid syntax! Proper syntax: //undo [steps] [account]");
				return;
			}

			int steps = 1;
			int ID = e.Player.User.ID;
			if (e.Parameters.Count > 0 && (!int.TryParse(e.Parameters[0], out steps) || steps <= 0))
				e.Player.SendErrorMessage("Invalid undo steps '{0}'!", e.Parameters[0]);
			else if (e.Parameters.Count > 1)
			{
				User User = TShock.Users.GetUserByName(e.Parameters[1]);
				if (User == null)
				{
					e.Player.SendErrorMessage("Invalid account name!");
					return;
				}
				ID = User.ID;
			}
			CommandQueue.Add(new Undo(e.Player, ID, steps));
		}
	}
}
