using Terraria;
using Terraria.GameContent.Tile_Entities;
using Terraria.ID;
using TShockAPI;

namespace WorldEdit.Commands
{
	public class Activate : WECommand
	{
		private readonly int _action;
		public Activate(int x, int y, int x2, int y2, TSPlayer plr, byte action)
			: base(x, y, x2, y2, plr)
		{
			_action = action;
		}

		public override void Execute()
		{
			string message;
			switch (_action)
			{
				case 0:
					{
						int success = 0, failure = 0;
						for (int i = x; i <= x2; i++)
						{
							for (int j = y; j <= y2; j++)
							{
								if ((Main.tile[i, j].type == TileID.Signs
									|| Main.tile[i, j].type == TileID.Tombstones
									|| Main.tile[i, j].type == TileID.AnnouncementBox)
									&& Main.tile[i, j].frameX % 36 == 0
									&& Main.tile[i, j].frameY == 0
									&& Sign.ReadSign(i, j, false) == -1)
								{
									int sign = Sign.ReadSign(i, j);
									if (sign == -1) failure++;
									else success++;
								}
							}
						}
						message = string.Format("Activated signs. ({0}){1}", success,
							failure > 0 ? " Failed to activate signs. (" + failure + ")" : "");
						break;
					}
				case 1:
					{
						int success = 0, failure = 0;
						for (int i = x; i <= x2; i++)
						{
							for (int j = y; j <= y2; j++)
							{
								if ((Main.tile[i, j].type == TileID.Containers
									|| Main.tile[i, j].type == TileID.Containers2
									|| Main.tile[i, j].type == TileID.Dressers)
									&& Main.tile[i, j].frameX % 36 == 0
									&& Main.tile[i, j].frameY == 0
									&& Chest.FindChest(i, j) == -1)
								{
									int chest = Chest.CreateChest(i, j);
									if (chest == -1) failure++;
									else success++;
								}
							}
						}
						message = string.Format("Activated chests. ({0}){1}", success,
							failure > 0 ? " Failed to activate chests. (" + failure + ")" : "");
						break;
					}
				case 2:
					{
						int success = 0, failure = 0;
						for (int i = x; i <= x2; i++)
						{
							for (int j = y; j <= y2; j++)
							{
								if (Main.tile[i, j].type == TileID.ItemFrame
									&& Main.tile[i, j].frameX % 36 == 0
									&& Main.tile[i, j].frameY == 0
									&& TEItemFrame.Find(i, j) == -1)
								{
									int frame = TEItemFrame.Place(i, j);
									if (frame == -1) failure++;
									else success++;
								}
							}
						}
						message = string.Format("Activated item frames. ({0}){1}", success,
							failure > 0 ? " Failed to activate item frames. (" + failure + ")" : "");
						break;
					}
				default: return;
			}
			ResetSection();
			plr.SendSuccessMessage(message);
		}
	}
}