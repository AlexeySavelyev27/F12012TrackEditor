#pragma once
#include "PSSGNode.h"
#include "PSSGSchema.h"
#include <string>

class PSSGWriter {
public:
    PSSGWriter(const PSSGNode& root);
    void Save(const std::string& path);
private:
    void WriteNode(std::ostream& stream, const PSSGNode& node);
    void ComputeSizes(PSSGNode& node);

    PSSGNode root_;
    PSSGSchema schema_;
};
