using Terraria;
using TShockAPI;
using WorldEdit.Expressions;

namespace WorldEdit.Commands
{
	public class Slope : WECommand
	{
		private Expression expression;
		private byte slope;

		public Slope(int x, int y, int x2, int y2, TSPlayer plr, int slope, Expression expression)
			: base(x, y, x2, y2, plr)
		{
			this.slope = (byte)slope;
			this.expression = expression ?? new TestExpression(new Test(t => true));
		}

		public override void Execute()
		{
			Tools.PrepareUndo(x, y, x2, y2, plr);
			int edits = 0;
			if (slope == 1)
			{
				for (int i = x; i <= x2; i++)
				{
					for (int j = y; j <= y2; j++)
					{
						var tile = Main.tile[i, j];
						if (tile.active() && select(i, j, plr) && expression.Evaluate(tile))
						{
							tile.halfBrick(true);
							edits++;
						}
					}
				}
			}
			else
			{
				if (slope > 1) { slope--; }
				for (int i = x; i <= x2; i++)
				{
					for (int j = y; j <= y2; j++)
					{
						var tile = Main.tile[i, j];
						if (tile.active() && select(i, j, plr) && expression.Evaluate(tile))
						{
							tile.slope(slope);
							edits++;
						}
					}
				}
			}
			ResetSection();
			plr.SendSuccessMessage("Sloped tiles. ({0})", edits);
		}
	}
}