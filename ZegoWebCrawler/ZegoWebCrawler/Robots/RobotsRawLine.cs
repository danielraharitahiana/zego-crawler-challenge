using System;
using System.Collections.Generic;
using System.Text;

namespace ZegoWebCrawler.Robots;

public readonly record struct RawLine(string Directive, string Value);