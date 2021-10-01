registerPlugin({
	name: "palettescreenshotpattern",
	version: "1.0",
	authors: ["kendfrey"],
	type: "local",
	licence: "MIT",
	main,
});

function main()
{
	if (ui !== undefined)
	{
		ui.registerMenuItem("Palette Screenshot Pattern", showPattern);
	}
}

const windowId = "palettescreenshotpattern";

function getWindow()
{
	return ui.getWindow(windowId);
}

function showPattern()
{
	const window = getWindow();
	if (window !== null)
	{
		window.bringToFront();
	}
	else
	{
		const windowDesc =
		{
			classification: windowId,
			width: 258,
			height: 17,
			title: "Palette Screenshot Pattern",
			widgets:
			[
				{
					type: "custom",
					x: 1,
					y: 15,
					width: 257,
					height: 2,
					onDraw: drawPattern,
				},
			],
		};
		ui.openWindow(windowDesc);
	}
}

function drawPattern(g)
{
	for (var i = 0; i < 256; i++)
	{
		g.fill = i;
		g.rect(i, 0, 1, 1);
	}
}