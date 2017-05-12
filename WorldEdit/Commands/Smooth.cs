using Terraria;
using TShockAPI;
using WorldEdit.Expressions;

namespace WorldEdit.Commands
{
	public class Smooth : WECommand
	{
		private Expression expression;
		private bool Plus;

		public Smooth(int x, int y, int x2, int y2, TSPlayer plr, Expression expression, bool Plus = false)
			: base(x, y, x2, y2, plr)
		{
			this.expression = expression ?? new TestExpression(new Test(t => true));
			this.Plus = Plus;
		}

		public override void Execute()
		{
			if (x < 2) x = 2;
			else if (x > (Main.maxTilesX - 3)) x = (Main.maxTilesX - 3);
			if (y < 2) y = 2;
			else if (y > (Main.maxTilesY - 3)) y = (Main.maxTilesY - 3);
			if (x2 < 2) x2 = 2;
			else if (x2 > (Main.maxTilesX - 3)) x2 = (Main.maxTilesX - 3);
			if (y2 < 2) y2 = 2;
			else if (y2 > (Main.maxTilesY - 3)) y2 = (Main.maxTilesY - 3);

			Tools.PrepareUndo(x, y, x2, y2, plr);
			int edits = 0;
			for (int i = x; i <= x2; i++)
			{
				for (int j = y; j <= y2; j++)
				{
					bool XY = Main.tile[i, j].active();
					bool slope = (Main.tile[i, j].slope() == 0);
					bool mXY = Main.tile[i - 1, j].active();
					bool pXY = Main.tile[i + 1, j].active();
					bool XmY = Main.tile[i, j - 1].active();
					bool XpY = Main.tile[i, j + 1].active();

					if (XY && slope && expression.Evaluate(Main.tile[i, j]))
					{
						if (mXY && XmY && !XpY && !pXY)
						{
							Main.tile[i, j].slope(3);
							edits++;
						}
						else if (XmY && pXY && !XpY && !mXY)
						{
							Main.tile[i, j].slope(4);
							edits++;
						}
						else if (pXY && XpY && !mXY && !XmY)
						{
							Main.tile[i, j].slope(2);
							edits++;
						}
						else if (XpY && mXY && !XmY && !pXY)
						{
							Main.tile[i, j].slope(1);
							edits++;
						}
						if (Plus)
						{
							if (mXY && XpY && pXY && !XmY && !Main.tile[i + 1, j - 1].active()
								&& ((Main.tile[i + 1, j + 1].active() && Main.tile[i + 2, j].active()
									&& Main.tile[i - 1, j - 1].active() && Main.tile[i + 2, j - 1].active())
										|| (!Main.tile[i - 1, j + 1].active() && !Main.tile[i + 2, j + 1].active()
											&& Main.tile[i + 1, j + 1].active() && Main.tile[i + 2, j].active())))
							{
								Main.tile[i, j].slope(1);
								Main.tile[i + 1, j].slope(2);
								edits += 2;
							}
							else if (XmY && mXY && XpY && !pXY && !Main.tile[i + 1, j + 1].active()
								&& ((Main.tile[i - 1, j + 1].active() && Main.tile[i, j + 2].active()
									&& Main.tile[i + 1, j - 1].active() && Main.tile[i + 1, j + 2].active())
										|| (!Main.tile[i - 1, j - 1].active() && !Main.tile[i - 1, j + 2].active()
											&& Main.tile[i - 1, j + 1].active() && Main.tile[i, j + 2].active())))
							{
								Main.tile[i, j].slope(3);
								Main.tile[i, j + 1].slope(1);
								edits += 2;
							}
							else if (pXY && XmY && mXY && !XpY && !Main.tile[i - 1, j + 1].active()
								&& ((Main.tile[i - 1, j - 1].active() && Main.tile[i - 2, j].active()
									&& Main.tile[i + 1, j + 1].active() && Main.tile[i - 2, j + 1].active())
										|| (!Main.tile[i + 1, j - 1].active() && !Main.tile[i - 2, j - 1].active()
											&& Main.tile[i - 1, j - 1].active() && Main.tile[i - 2, j].active())))
							{
								Main.tile[i, j].slope(4);
								Main.tile[i - 1, j].slope(3);
								edits += 2;
							}
							else if (XpY && pXY && XmY && !mXY && !Main.tile[i - 1, j - 1].active()
								&& ((Main.tile[i + 1, j - 1].active() && Main.tile[i, j - 2].active()
									&& Main.tile[i - 1, j + 1].active() && Main.tile[i - 1, j - 2].active())
										|| (!Main.tile[i + 1, j + 1].active() && !Main.tile[i + 1, j - 2].active()
											&& Main.tile[i + 1, j - 1].active() && Main.tile[i, j - 2].active())))
							{
								Main.tile[i, j].slope(2);
								Main.tile[i, j - 1].slope(4);
								edits += 2;
							}
						}
					}
				}
			}
			ResetSection();
			plr.SendSuccessMessage("Smoothed area. ({0})", edits);
		}
	}
}