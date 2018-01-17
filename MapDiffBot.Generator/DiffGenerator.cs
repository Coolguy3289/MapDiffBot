﻿using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MapDiffBot.Generator
{
	/// <inheritdoc />
	sealed class DiffGenerator : IGenerator
	{
		/// <summary>
		/// Generate the dmm-tools.exe command line arguments with the exception of the .dmm paramter which can be formatted in
		/// </summary>
		/// <param name="region">The <see cref="DiffRegion"/> to put on the command line, if any</param>
		/// <returns>The dmm-tools.exe command line arguments</returns>
		static string GenerateCommandLine(DiffRegion region)
		{
			const string minimapParam = "--disable hide-space --minimap \"{0}\"";
			if (region == null)
				return minimapParam;
			return String.Format(CultureInfo.InvariantCulture, "--min {0},{1} --max {2},{3} {4}", region.MinX, region.MaxX, region.MaxX, region.MaxY, minimapParam);
		}

		/// <summary>
		/// Extract the dmm-tools.exe from the running <see cref="System.Reflection.Assembly"/> and return the path to it
		/// </summary>
		static async Task<string> GetDMMToolsPath()
		{
			//TODO
			return await Task.FromResult("S:/Documents/Actual Documents/DA Git/SpacemanDMM/target/debug/dmm-tools.exe");
		}

		/// <summary>
		/// Renders the map at <paramref name="mapPath"/> with the given <paramref name="region"/>
		/// </summary>
		/// <param name="mapPath">The path of the map to render. Must be among associated codebase files</param>
		/// <param name="workingDirectory">The path that contains the .dme for the .dmm</param>
		/// <param name="region">The <see cref="DiffRegion"/> to render, if any</param>
		/// <param name="token">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> resulting in the path to the rendered .png file</returns>
		static async Task<string> RenderMap(string mapPath, string workingDirectory, DiffRegion region, CancellationToken token)
		{
			var output = new StringBuilder();
			var errorOutput = new StringBuilder();
			using (var P = new Process())
			{
				P.StartInfo.Arguments = String.Format(CultureInfo.InvariantCulture, GenerateCommandLine(region), mapPath);
				P.StartInfo.RedirectStandardOutput = true;
				P.StartInfo.RedirectStandardError = true;
				P.StartInfo.UseShellExecute = false;
				P.StartInfo.WorkingDirectory = workingDirectory;
				P.OutputDataReceived += new DataReceivedEventHandler(
					delegate (object sender, DataReceivedEventArgs e)
					{
						output.Append(Environment.NewLine);
						output.Append(e.Data);
					}
				);
				P.ErrorDataReceived += new DataReceivedEventHandler(
					delegate (object sender, DataReceivedEventArgs e)
					{
						errorOutput.Append(Environment.NewLine);
						errorOutput.Append(e.Data);
					}
				);

				P.StartInfo.FileName = await GetDMMToolsPath();

				var tcs = new TaskCompletionSource<object>();
				P.EnableRaisingEvents = true;
				P.Exited += (a, b) =>
				{
					if (token.IsCancellationRequested)
						tcs.SetCanceled();
					else
						tcs.SetResult(null);
				};

				P.Start();
				P.BeginOutputReadLine();
				P.BeginErrorReadLine();
				using (var reg = token.Register(() =>
				{
					try
					{
						P.Kill();
					}
					catch (InvalidOperationException) { }
				}))
					await tcs.Task;

				if (P.ExitCode != 0)
					throw new GeneratorException(String.Format(CultureInfo.CurrentCulture, "dmm-tools.exe exited with error code {0}! Output:{1}{2}{1}Error:{1}{3}", P.ExitCode, Environment.NewLine, output.ToString(), errorOutput.ToString()));
			}
			
			bool expectNext = false;
			foreach (var I in output.ToString().Split(' '))
			{
				var text = I.Trim();
				if (text == "saving")
					expectNext = true;
				else if (expectNext && text.EndsWith(".png"))
					return text;
				else
					expectNext = false;
			}

			throw new GeneratorException(String.Format(CultureInfo.CurrentCulture, "Unable to find .png file in dmm-tools output! Output:{0}{1}{0}Error:{0}{2}", Environment.NewLine, output.ToString(), errorOutput.ToString()));
		}

		/// <summary>
		/// Generate a <see cref="DiffRegion"/> given two map files with a common ancestor
		/// </summary>
		/// <param name="mapPathA">The path to the "before" map</param>
		/// <param name="mapPathB">The path to the "after" map</param>
		/// <param name="workingDirectory">The path that contains the .dme for the .dmms</param>
		/// <param name="token">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> resulting in a <see cref="DiffRegion"/> for the two maps</returns>
		[SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "workingDirectory")]
		[SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "mapPathA")]
		[SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "mapPathB")]
		[SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "token")]
		static async Task<DiffRegion> GetDiffRegion(string mapPathA, string mapPathB, string workingDirectory, CancellationToken token)
		{
			//TODO
			return await Task.FromResult<DiffRegion>(null);
		}

		/// <summary>
		/// Clean up the extracted dmm-tools.exe
		/// </summary>
		public void Dispose()
		{
			//TODO
		}

		/// <inheritdoc />
		public async Task<IMapDiff> GenerateDiff(string mapPathA, string mapPathB, string workingDirectory, string outputDirectory, CancellationToken token)
		{
			var region = await GetDiffRegion(mapPathA, mapPathB, workingDirectory, token);

			Directory.CreateDirectory(Path.Combine(workingDirectory, "data", "minimaps"));
			
			var mA = RenderMap(mapPathA, workingDirectory, region, token);
			var mB = RenderMap(mapPathB, workingDirectory, region, token);

			var mapName = Path.GetFileNameWithoutExtension(mapPathA.Length > mapPathB.Length ? mapPathB : mapPathA);

			var outputA = Path.Combine(workingDirectory, await mA);
			var outputB = Path.Combine(workingDirectory, await mB);

			var movedA = Path.Combine(outputDirectory, String.Format(CultureInfo.InvariantCulture, "{0}.before.png", mapName));
			var movedB = Path.Combine(outputDirectory, String.Format(CultureInfo.InvariantCulture, "{0}.after.png", mapName));

			File.Move(outputA, movedA);
			File.Move(outputB, movedB);

			return new MapDiff(mapName, movedA, movedB);
		}
	}
}
